using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresMaterialisation
    {
        /// <summary>Nombre de chaînes de frontière promues en massifs, tiré dans cet intervalle.</summary>
        public int NbMassifsMin = 6;
        public int NbMassifsMax = 10;

        /// <summary>Part maximale de cellules non constructibles (massif + lac) dans une zone.</summary>
        public float PartNonConstructibleMax = 0.45f;

        public int LacCellulesMin = 3;
        public int LacCellulesMax = 6;
        public int NbLacsMax = 3;
        public float PartLacMaxParZone = 0.25f;

        /// <summary>Pente maximale hors massif, en degrés.</summary>
        public float PenteMaxDegres = 40f;

        /// <summary>Pente maximale des faces de massif, en degrés.</summary>
        public float PenteMassifMaxDegres = 60f;

        /// <summary>Hauteur ajoutée au sommet d'un massif, en mètres.</summary>
        public float HauteurMassif = 18f;

        /// <summary>Creusement d'un lit de rivière matérialisée, en mètres.</summary>
        public float ProfondeurRiviere = 2f;

        /// <summary>Altitude sous laquelle un lit ne peut plus descendre.</summary>
        public float PlancherRiviere = 1.5f;

        public int SocketsMinesMinParMassif = 2;
        public int SocketsMinesMaxParMassif = 4;
        public float DistanceMinSockets = 25f;

        /// <summary>Distance à l'eau sous laquelle une cellule peut porter un socket de pêche.</summary>
        public float DistancePeche = 12f;
        public int SocketsPecheMaxParZone = 3;
    }

    public sealed class DiagnosticMaterialisation
    {
        public bool Reussi;
        public string MotifEchec;
        public int NbSegments;
        public int NbSegmentsMassif;
        public int NbSegmentsRiviere;
        public int NbSegmentsRiviereProlongee;
        public int NbMassifs;
        public int NbLacs;
        public int NbCellulesMassif;
        public int NbCellulesLac;
        public float PartNonConstructibleMax;
        public int NbZonesAmincies;

        /// <summary>Cellules de massif rendues praticables pour recoller une zone ou un groupe.</summary>
        public int NbCellulesLiberees;
        public int NbAretesNonBloquantes;

        /// <summary>Arêtes inter-groupes hors chaîne principale, bloquées par le balayage de sûreté.</summary>
        public int NbAretesOrphelines;
        public int NbGroupesCoupes;
        public int NbZonesCoupees;
        public int NbSocketsMines;
        public int NbSocketsPeche;
        public int NbZonesRiveraines;
        public float PenteMaxHorsMassif;
        public long Millisecondes;

        public override string ToString()
        {
            if (!Reussi) return $"matérialisation ÉCHEC : {MotifEchec}";
            return $"{NbSegments} segments ({NbSegmentsMassif} massifs, {NbSegmentsRiviere} rivières, " +
                   $"{NbSegmentsRiviereProlongee} rivières prolongées) → {NbMassifs} massifs " +
                   $"({NbCellulesMassif} cellules), {NbLacs} lacs ({NbCellulesLac} cellules) ; " +
                   $"non constructible au plus {PartNonConstructibleMax:P0} ({NbZonesAmincies} zones amincies), " +
                   $"{NbAretesNonBloquantes} arêtes non bloquantes, {NbZonesCoupees} zones et " +
                   $"{NbGroupesCoupes} groupes coupés, {NbSocketsMines} sockets de mine et " +
                   $"{NbSocketsPeche} de pêche, {NbZonesRiveraines} zones riveraines, " +
                   $"pente max {PenteMaxHorsMassif:F0}°, {Millisecondes} ms";
        }
    }

    /// <summary>Suite continue d'arêtes inter-groupes, hors passage, à matérialiser d'un seul tenant.</summary>
    public sealed class SegmentFrontiere
    {
        public int GroupeA;
        public int GroupeB;
        public int[] Aretes;
        public int[] Coins;
        public float Longueur;
        public float Crete;
        public float PartRiviere;
        public TypeFrontiere Type = TypeFrontiere.Libre;

        /// <summary>Zone désignée « Montagne » pour un segment de type massif, sinon -1.</summary>
        public int ZoneMontagne = -1;

        /// <summary>Cours d'eau sans débouché : une mare sera posée à son extrémité basse.</summary>
        public bool BesoinDeMare;

        public int[] Cellules = Array.Empty<int>();
    }

    /// <summary>
    /// Phase P8 : matérialisation des frontières de groupes.
    ///
    /// C'est ici que la structure devient géographie. Chaque frontière entre deux groupes,
    /// hors passage, doit devenir un obstacle réel — sans quoi la règle « ce qui sépare deux
    /// groupes bloque » ne serait qu'une convention affichée. Trois natures d'obstacle, dans
    /// cet ordre de priorité :
    ///
    /// — les segments que l'hydrologie a déjà dotés d'une rivière la gardent ;
    /// — les segments les plus élevés, classés par leur champ de crêtes, deviennent des
    ///   massifs : une bande de cellules non constructibles, surélevée ;
    /// — les autres reçoivent une rivière prolongée, creusée en descendant vers celle de
    ///   leurs extrémités qui rejoint déjà la mer, un lac ou un autre cours d'eau.
    ///
    /// Les rivières ne consomment aucune cellule, seulement du relief : c'est ce qui permet
    /// de garder plus de la moitié de chaque zone constructible tout en bloquant la totalité
    /// des frontières. Les massifs, eux, mangent des cellules, d'où le plafond et
    /// l'amincissement des bandes qui suit.
    /// </summary>
    public static class Materialisation
    {
        public const int Phase = 9;

        public static bool Construire(Carte carte, ParametresMaterialisation p, Rng rngRacine,
                                      out DiagnosticMaterialisation diag)
        {
            if (carte?.Passages == null) throw new InvalidOperationException("Les passages doivent précéder la matérialisation.");

            var chrono = System.Diagnostics.Stopwatch.StartNew();
            diag = new DiagnosticMaterialisation();
            var rng = rngRacine.Deriver(Phase);
            GrapheCellules g = carte.Graphe;

            carte.Drapeaux = new ushort[g.NbCellules];
            carte.TypeArete = new TypeFrontiere[g.NbAretes];
            carte.AreteBloquante = new bool[g.NbAretes];

            for (int c = 0; c < g.NbCellules; c++)
            {
                if (carte.Terre[c]) Marquer(carte, c, DrapeauxCellule.Terre);
                if (carte.Terre[c] && carte.DistCote[c] == 0) Marquer(carte, c, DrapeauxCellule.Plage);
                if (carte.CelluleReservee[c]) Marquer(carte, c, DrapeauxCellule.Reserve);
            }

            // La mer bloque par nature, et les passages sont ouverts par nature.
            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b])
                {
                    carte.TypeArete[e] = TypeFrontiere.Mer;
                    carte.AreteBloquante[e] = true;
                }
            }
            foreach (Passage passage in carte.Passages)
            {
                foreach (int e in passage.AretesFines) carte.TypeArete[e] = TypeFrontiere.Col;
            }

            List<SegmentFrontiere> segments = DecouperSegments(carte);
            diag.NbSegments = segments.Count;

            TyperSegments(carte, p, segments, ref rng, diag);
            PoserMassifs(carte, p, segments, diag);
            CreuserRivieres(carte, p, segments);
            PoserLacs(carte, p, diag);

            diag.NbCellulesLiberees = ReparerPraticabilite(carte);
            NettoyerLacs(carte);
            diag.NbLacs = carte.Lacs.Count;
            AppliquerPlafondConstructible(carte, p, segments, diag);
            MarquerBlocages(carte, segments, diag);

            VerifierPraticabilite(carte, diag);
            LimiterPente(carte, p, diag);
            PoserSockets(carte, p, segments, diag);
            MarquerRiveraines(carte, diag);

            carte.Segments = segments;
            diag.Reussi = diag.NbAretesNonBloquantes == 0 && diag.NbZonesCoupees == 0 && diag.NbGroupesCoupes == 0;
            if (!diag.Reussi)
            {
                diag.MotifEchec = diag.NbAretesNonBloquantes > 0
                    ? $"{diag.NbAretesNonBloquantes} arêtes inter-groupes ne bloquent pas"
                    : $"{diag.NbZonesCoupees} zones et {diag.NbGroupesCoupes} groupes coupés en deux";
            }
            diag.Millisecondes = chrono.ElapsedMilliseconds;
            return diag.Reussi;
        }

        static void Marquer(Carte carte, int cellule, DrapeauxCellule drapeau)
        {
            carte.Drapeaux[cellule] |= (ushort)drapeau;
        }

        static bool A(Carte carte, int cellule, DrapeauxCellule drapeau)
        {
            return (carte.Drapeaux[cellule] & (ushort)drapeau) != 0;
        }

        // ------------------------------------------------------------------ segments

        /// <summary>
        /// Coupe chaque frontière de groupes en segments continus, en retirant les arêtes
        /// occupées par un passage. Un passage laisse donc de part et d'autre deux segments
        /// à matérialiser séparément.
        /// </summary>
        static List<SegmentFrontiere> DecouperSegments(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            var segments = new List<SegmentFrontiere>();

            foreach (FrontiereGroupes f in carte.FrontieresGroupes)
            {
                var dansPassage = new bool[f.Chaine.Length];
                if (f.Passage >= 0)
                {
                    var aretesPassage = new HashSet<int>(carte.Passages[f.Passage].AretesFines);
                    for (int i = 0; i < f.Chaine.Length; i++) dansPassage[i] = aretesPassage.Contains(f.Chaine[i]);
                }

                int debut = 0;
                while (debut < f.Chaine.Length)
                {
                    if (dansPassage[debut]) { debut++; continue; }
                    int fin = debut;
                    while (fin < f.Chaine.Length && !dansPassage[fin]) fin++;

                    var aretes = new int[fin - debut];
                    Array.Copy(f.Chaine, debut, aretes, 0, aretes.Length);
                    var coins = new int[aretes.Length + 1];
                    Array.Copy(f.Coins, debut, coins, 0, coins.Length);

                    double longueur = 0.0, crete = 0.0, riviere = 0.0;
                    foreach (int e in aretes)
                    {
                        float l = g.LongueurArete[e];
                        longueur += l;
                        crete += l * 0.5f * (carte.Crete[g.AreteCelluleA[e]] + carte.Crete[g.AreteCelluleB[e]]);
                        if (carte.AreteRiviere[e]) riviere += l;
                    }

                    segments.Add(new SegmentFrontiere
                    {
                        GroupeA = f.GroupeA,
                        GroupeB = f.GroupeB,
                        Aretes = aretes,
                        Coins = coins,
                        Longueur = (float)longueur,
                        Crete = longueur > 0.0 ? (float)(crete / longueur) : 0f,
                        PartRiviere = longueur > 0.0 ? (float)(riviere / longueur) : 0f
                    });

                    debut = fin;
                }
            }
            return segments;
        }

        /// <summary>
        /// Attribue à chaque segment sa nature. Les rivières existantes sont respectées, les
        /// segments les plus élevés deviennent des massifs, le reste reçoit un cours d'eau
        /// creusé — à condition qu'une de ses extrémités ait un exutoire, faute de quoi le
        /// segment devient massif à son tour plutôt que de porter une rivière qui ne mène
        /// nulle part.
        /// </summary>
        static void TyperSegments(Carte carte, ParametresMaterialisation p, List<SegmentFrontiere> segments,
                                  ref Rng rng, DiagnosticMaterialisation diag)
        {
            var restants = new List<int>();
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].Aretes.Length == 0) continue;
                if (segments[i].PartRiviere >= 0.5f)
                {
                    segments[i].Type = TypeFrontiere.Riviere;
                    diag.NbSegmentsRiviere++;
                }
                else restants.Add(i);
            }

            restants.Sort((a, b) =>
            {
                float ca = segments[a].Crete, cb = segments[b].Crete;
                if (ca != cb) return cb < ca ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            int nbMassifs = rng.Entier(p.NbMassifsMin, p.NbMassifsMax + 1);
            nbMassifs = Math.Min(nbMassifs, restants.Count);

            // Tout ce qui n'est pas massif devient cours d'eau. Le premier essai réservait la
            // rivière prolongée aux segments dont une extrémité rejoignait déjà la mer, un lac
            // ou un autre cours d'eau, et basculait le reste en massif : comme la plupart des
            // frontières intérieures n'ont aucun exutoire, presque toute l'île devenait
            // montagne et une zone sur quatre se retrouvait coupée en deux. Un ruisseau sans
            // débouché n'est pas absurde — il finit en mare, ce que P8 pose à son extrémité
            // basse — alors qu'une chaîne de montagnes de trop l'est.
            for (int k = 0; k < restants.Count; k++)
            {
                SegmentFrontiere s = segments[restants[k]];
                if (k < nbMassifs)
                {
                    s.Type = TypeFrontiere.Massif;
                    diag.NbSegmentsMassif++;
                }
                else
                {
                    s.Type = TypeFrontiere.RiviereProlongee;
                    diag.NbSegmentsRiviereProlongee++;
                }
            }

            // Les débouchés se décident une fois tous les types connus : un ruisseau se jette
            // le plus souvent dans un autre, et non dans la mer. Évaluer cela segment par
            // segment au fil du typage donnait une mare à presque chacun d'eux.
            var confluences = new Dictionary<int, int>();
            foreach (SegmentFrontiere s in segments)
            {
                if (s.Type != TypeFrontiere.Riviere && s.Type != TypeFrontiere.RiviereProlongee) continue;
                foreach (int coin in new[] { s.Coins[0], s.Coins[s.Coins.Length - 1] })
                {
                    confluences.TryGetValue(coin, out int n);
                    confluences[coin] = n + 1;
                }
            }

            foreach (SegmentFrontiere s in segments)
            {
                if (s.Type != TypeFrontiere.RiviereProlongee) continue;
                s.BesoinDeMare = !Debouche(carte, confluences, s.Coins[0])
                              && !Debouche(carte, confluences, s.Coins[s.Coins.Length - 1]);
            }
        }

        static bool Debouche(Carte carte, Dictionary<int, int> confluences, int coin)
        {
            if (EstExutoire(carte, coin)) return true;
            return confluences.TryGetValue(coin, out int n) && n >= 2;
        }

        static bool EstExutoire(Carte carte, int coin)
        {
            if (carte.CoinRiviere[coin]) return true;
            GrapheCellules g = carte.Graphe;
            for (int i = g.DebutCellulesDeCoin[coin]; i < g.DebutCellulesDeCoin[coin + 1]; i++)
            {
                int cellule = g.CellulesDeCoin[i];
                if (!carte.Terre[cellule]) return true;
                if (A(carte, cellule, DrapeauxCellule.Lac)) return true;
            }
            return false;
        }

        // -------------------------------------------------------------------- massifs

        /// <summary>
        /// Pose une bande de cellules le long de chaque segment de massif, plus épaisse du
        /// côté de la zone qui portera la montagne. Les cellules réservées par un passage
        /// sont sautées : un col ouvert doit le rester.
        /// </summary>
        static void PoserMassifs(Carte carte, ParametresMaterialisation p, List<SegmentFrontiere> segments,
                                 DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;

            // Nombre de cellules encore posables par zone avant d'atteindre le plafond de
            // non-constructible. On garde une marge : les lacs viendront après.
            var quota = new int[carte.NbZones];
            for (int z = 0; z < carte.NbZones; z++)
            {
                quota[z] = (int)(carte.CellulesDeZone[z].Count * p.PartNonConstructibleMax * 0.75f);
            }

            foreach (SegmentFrontiere s in segments)
            {
                if (s.Type != TypeFrontiere.Massif) continue;

                // Zone « Montagne » du massif : celle qui borde le segment sur la plus grande
                // longueur. C'est elle qui portera le terrain Montagne et les mines.
                var longueurParZone = new Dictionary<int, float>();
                foreach (int e in s.Aretes)
                {
                    float l = g.LongueurArete[e];
                    Ajouter(longueurParZone, carte.ZoneDeCellule[g.AreteCelluleA[e]], l);
                    Ajouter(longueurParZone, carte.ZoneDeCellule[g.AreteCelluleB[e]], l);
                }
                int zoneMontagne = -1;
                float meilleure = -1f;
                foreach (KeyValuePair<int, float> paire in longueurParZone)
                {
                    if (paire.Key < 0) continue;
                    if (paire.Value > meilleure || (paire.Value == meilleure && paire.Key < zoneMontagne))
                    {
                        meilleure = paire.Value;
                        zoneMontagne = paire.Key;
                    }
                }
                s.ZoneMontagne = zoneMontagne;

                var cellules = new SortedSet<int>();
                foreach (int e in s.Aretes)
                {
                    if (AjouterSiPossible(carte, p, quota, cellules, g.AreteCelluleA[e])) { }
                    if (AjouterSiPossible(carte, p, quota, cellules, g.AreteCelluleB[e])) { }
                }

                // Une rangée de plus du côté de la zone Montagne : une crête n'est pas
                // symétrique, elle domine un versant. Le quota par zone est vérifié à chaque
                // ajout plutôt qu'en retirant après coup — une bande posée puis amincie
                // laisse des brèches, une bande qui s'arrête à temps n'en laisse aucune.
                var premiereRangee = new List<int>(cellules);
                foreach (int c in premiereRangee)
                {
                    if (carte.ZoneDeCellule[c] != zoneMontagne) continue;
                    for (int slot = g.DebutCoins[c]; slot < g.DebutCoins[c + 1]; slot++)
                    {
                        int v = g.VoisinsDeCellule[slot];
                        if (v < 0 || carte.ZoneDeCellule[v] != zoneMontagne) continue;
                        AjouterSiPossible(carte, p, quota, cellules, v);
                    }
                }

                var tableau = new int[cellules.Count];
                cellules.CopyTo(tableau);
                s.Cellules = tableau;

                foreach (int c in tableau)
                {
                    Marquer(carte, c, DrapeauxCellule.Massif);
                    Marquer(carte, c, DrapeauxCellule.NonConstructible);
                }
                if (tableau.Length > 0) diag.NbMassifs++;
            }

            void Ajouter(Dictionary<int, float> table, int zone, float valeur)
            {
                if (zone < 0) return;
                table.TryGetValue(zone, out float actuel);
                table[zone] = actuel + valeur;
            }
        }

        static bool AjouterSiPossible(Carte carte, ParametresMaterialisation p, int[] quota,
                                      SortedSet<int> ensemble, int cellule)
        {
            if (cellule < 0 || !carte.Terre[cellule]) return false;
            if (carte.CelluleReservee[cellule]) return false;      // cellule de passage : intouchable
            if (ensemble.Contains(cellule)) return false;

            int zone = carte.ZoneDeCellule[cellule];
            if (zone < 0 || quota[zone] <= 0) return false;

            ensemble.Add(cellule);
            quota[zone]--;
            return true;
        }

        // ------------------------------------------------------------------ rivières

        /// <summary>
        /// Creuse le lit des segments aquatiques en descendant vers l'extrémité la plus
        /// basse, sans jamais passer sous le plancher : une rivière qui remonterait ou qui
        /// plongerait sous le niveau de la mer ne serait pas crédible et fausserait le
        /// maillage 3D.
        /// </summary>
        static void CreuserRivieres(Carte carte, ParametresMaterialisation p, List<SegmentFrontiere> segments)
        {
            foreach (SegmentFrontiere s in segments)
            {
                if (s.Type != TypeFrontiere.Riviere && s.Type != TypeFrontiere.RiviereProlongee) continue;

                int premier = s.Coins[0];
                int dernier = s.Coins[s.Coins.Length - 1];
                bool versLaFin = carte.HauteurCoin[dernier] <= carte.HauteurCoin[premier];

                float precedente = float.MaxValue;
                for (int k = 0; k < s.Coins.Length; k++)
                {
                    int index = versLaFin ? k : s.Coins.Length - 1 - k;
                    int coin = s.Coins[index];

                    float cible = carte.HauteurCoin[coin] - p.ProfondeurRiviere;
                    if (cible > precedente - 0.05f) cible = precedente - 0.05f;
                    if (cible < p.PlancherRiviere && carte.HauteurCoin[coin] > p.PlancherRiviere)
                    {
                        cible = p.PlancherRiviere;
                    }
                    if (cible < carte.HauteurCoin[coin]) carte.HauteurCoin[coin] = cible;
                    precedente = carte.HauteurCoin[coin];
                }

                if (s.BesoinDeMare)
                {
                    int extremiteBasse = versLaFin ? dernier : premier;
                    PoserMare(carte, p, extremiteBasse);
                }
            }
        }

        /// <summary>
        /// Un cours d'eau sans débouché finit en mare plutôt que de s'arrêter à sec. Deux ou
        /// trois cellules suffisent : c'est un point d'eau, pas un lac.
        /// </summary>
        static void PoserMare(Carte carte, ParametresMaterialisation p, int coin)
        {
            GrapheCellules g = carte.Graphe;
            var candidates = new List<int>();

            for (int i = g.DebutCellulesDeCoin[coin]; i < g.DebutCellulesDeCoin[coin + 1]; i++)
            {
                int c = g.CellulesDeCoin[i];
                if (!carte.Terre[c] || carte.CelluleReservee[c]) continue;
                if (A(carte, c, DrapeauxCellule.Massif | DrapeauxCellule.Lac)) continue;
                int z = carte.ZoneDeCellule[c];
                if (z < 0) continue;

                // Une mare ne doit pas non plus faire déborder le plafond de sa zone.
                int nonConstructibles = 0;
                foreach (int autre in carte.CellulesDeZone[z])
                {
                    if (A(carte, autre, DrapeauxCellule.NonConstructible)) nonConstructibles++;
                }
                if (nonConstructibles + 1 > carte.CellulesDeZone[z].Count * p.PartNonConstructibleMax) continue;

                candidates.Add(c);
                if (candidates.Count >= 2) break;
            }
            if (candidates.Count == 0) return;

            float niveau = carte.HauteurCoin[coin];
            foreach (int c in candidates)
            {
                Marquer(carte, c, DrapeauxCellule.Lac);
                Marquer(carte, c, DrapeauxCellule.NonConstructible);
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int sommet = g.CoinsDeCellule[s];
                    carte.HauteurCoin[sommet] = math.min(carte.HauteurCoin[sommet], niveau);
                }
            }
            carte.Lacs.Add(new LacData { Cellules = candidates.ToArray(), Niveau = niveau });
        }

        // ---------------------------------------------------------------------- lacs

        static void PoserLacs(Carte carte, ParametresMaterialisation p, DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;
            int poses = 0;

            foreach (int[] bassin in carte.BassinsCandidats)
            {
                if (poses >= p.NbLacsMax) break;
                if (bassin.Length < p.LacCellulesMin || bassin.Length > p.LacCellulesMax) continue;

                bool acceptable = true;
                var parZone = new Dictionary<int, int>();
                foreach (int c in bassin)
                {
                    if (carte.CelluleReservee[c] || A(carte, c, DrapeauxCellule.Massif)) { acceptable = false; break; }
                    int z = carte.ZoneDeCellule[c];
                    if (z < 0) { acceptable = false; break; }
                    parZone.TryGetValue(z, out int n);
                    parZone[z] = n + 1;
                }
                if (!acceptable) continue;

                foreach (KeyValuePair<int, int> paire in parZone)
                {
                    List<int> cellulesZone = carte.CellulesDeZone[paire.Key];
                    if (paire.Value > cellulesZone.Count * p.PartLacMaxParZone) { acceptable = false; break; }

                    // Le lac doit aussi tenir dans le plafond GLOBAL de non-constructible de
                    // sa zone, massifs compris. Ne vérifier que sa part propre laissait passer
                    // des zones à plus de 50 % d'inconstructible.
                    int dejaPris = 0;
                    foreach (int autre in cellulesZone)
                    {
                        if (A(carte, autre, DrapeauxCellule.NonConstructible)) dejaPris++;
                    }
                    if (dejaPris + paire.Value > cellulesZone.Count * p.PartNonConstructibleMax)
                    {
                        acceptable = false;
                        break;
                    }
                }
                if (!acceptable) continue;

                float niveau = float.MaxValue;
                foreach (int c in bassin)
                {
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        niveau = math.min(niveau, carte.HauteurCoin[g.CoinsDeCellule[s]]);
                    }
                }

                foreach (int c in bassin)
                {
                    Marquer(carte, c, DrapeauxCellule.Lac);
                    Marquer(carte, c, DrapeauxCellule.NonConstructible);
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int coin = g.CoinsDeCellule[s];
                        carte.HauteurCoin[coin] = math.min(carte.HauteurCoin[coin], niveau - 1.5f);
                    }
                }
                carte.Lacs.Add(new LacData { Cellules = bassin, Niveau = niveau - 0.5f });
                poses++;
            }
            diag.NbLacs = poses;
        }

        // ------------------------------------------------------- plafond constructible

        /// <summary>
        /// Ramène chaque zone sous le plafond de cellules non constructibles en amincissant
        /// ses bandes de massif — jamais en changeant une cellule de zone, ce qui casserait
        /// le découpage validé en P5. On retire les cellules les plus éloignées du segment,
        /// c'est-à-dire les épaississements ajoutés côté zone Montagne.
        /// </summary>
        static void AppliquerPlafondConstructible(Carte carte, ParametresMaterialisation p,
                                                  List<SegmentFrontiere> segments, DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;

            var celluleDuSegment = new Dictionary<int, List<SegmentFrontiere>>();
            foreach (SegmentFrontiere s in segments)
            {
                foreach (int c in s.Cellules)
                {
                    if (!celluleDuSegment.TryGetValue(c, out List<SegmentFrontiere> liste))
                    {
                        liste = new List<SegmentFrontiere>(2);
                        celluleDuSegment[c] = liste;
                    }
                    liste.Add(s);
                }
            }

            for (int z = 0; z < carte.NbZones; z++)
            {
                List<int> cellules = carte.CellulesDeZone[z];
                int plafond = (int)(cellules.Count * p.PartNonConstructibleMax);

                var nonConstructibles = new List<int>();
                foreach (int c in cellules)
                {
                    if (A(carte, c, DrapeauxCellule.NonConstructible)) nonConstructibles.Add(c);
                }
                if (nonConstructibles.Count <= plafond) continue;

                // On ne retire que du massif : un lac ne s'amincit pas, il se remplit d'eau.
                var retirables = new List<int>();
                foreach (int c in nonConstructibles)
                {
                    if (A(carte, c, DrapeauxCellule.Massif) && !A(carte, c, DrapeauxCellule.Lac)) retirables.Add(c);
                }

                // Les cellules qui ne touchent aucune arête de leur segment sont les
                // épaississements : elles s'enlèvent sans ouvrir de brèche.
                retirables.Sort((a, b) =>
                {
                    int pa = ToucheSonSegment(carte, celluleDuSegment, a) ? 1 : 0;
                    int pb = ToucheSonSegment(carte, celluleDuSegment, b) ? 1 : 0;
                    if (pa != pb) return pa - pb;
                    return a < b ? -1 : (a > b ? 1 : 0);
                });

                // On retire d'abord les épaississements, qui ne bordent aucune arête de leur
                // segment et s'enlèvent sans ouvrir de brèche. Si cela ne suffit pas, on entame
                // la première rangée : la brèche que cela crée sera rattrapée par
                // MarquerBlocages, qui retypera le segment percé en cours d'eau. Un gué de plus
                // vaut mieux qu'une zone bâtie à moins de la moitié.
                int aRetirer = nonConstructibles.Count - plafond;
                int retirees = 0;
                foreach (int c in retirables)
                {
                    if (retirees >= aRetirer) break;
                    if (cellules.Count - (nonConstructibles.Count - retirees) < 3) break;
                    Liberer(carte, c);
                    retirees++;
                }
                if (retirees > 0) diag.NbZonesAmincies++;
            }

            // Comptage final.
            float pire = 0f;
            for (int z = 0; z < carte.NbZones; z++)
            {
                int n = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (A(carte, c, DrapeauxCellule.NonConstructible)) n++;
                }
                pire = math.max(pire, (float)n / carte.CellulesDeZone[z].Count);
            }
            diag.PartNonConstructibleMax = pire;

            for (int c = 0; c < g.NbCellules; c++)
            {
                if (A(carte, c, DrapeauxCellule.Massif)) diag.NbCellulesMassif++;
                if (A(carte, c, DrapeauxCellule.Lac)) diag.NbCellulesLac++;
            }
        }

        static bool ToucheSonSegment(Carte carte, Dictionary<int, List<SegmentFrontiere>> table, int cellule)
        {
            if (!table.TryGetValue(cellule, out List<SegmentFrontiere> liste)) return false;
            GrapheCellules g = carte.Graphe;
            foreach (SegmentFrontiere s in liste)
            {
                foreach (int e in s.Aretes)
                {
                    if (g.AreteCelluleA[e] == cellule || g.AreteCelluleB[e] == cellule) return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ blocages

        /// <summary>
        /// Marque les arêtes bloquantes et compte celles qui ne le sont pas. Une arête bloque
        /// si l'une de ses cellules est massif ou lac, ou si elle porte un cours d'eau.
        /// Là où l'amincissement a ouvert une brèche, on retype le segment en rivière : mieux
        /// vaut un gué de plus qu'une frontière franchissable en pleine plaine.
        /// </summary>
        static void MarquerBlocages(Carte carte, List<SegmentFrontiere> segments, DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;

            foreach (SegmentFrontiere s in segments)
            {
                bool aquatique = s.Type == TypeFrontiere.Riviere || s.Type == TypeFrontiere.RiviereProlongee;
                foreach (int e in s.Aretes)
                {
                    carte.TypeArete[e] = s.Type;
                    if (aquatique) { carte.AreteBloquante[e] = true; continue; }

                    int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                    bool bloque = (a >= 0 && A(carte, a, DrapeauxCellule.Massif | DrapeauxCellule.Lac))
                               || (b >= 0 && A(carte, b, DrapeauxCellule.Massif | DrapeauxCellule.Lac));
                    carte.AreteBloquante[e] = bloque;
                }
            }

            // Réparation : un segment qui laisse passer devient un cours d'eau.
            foreach (SegmentFrontiere s in segments)
            {
                if (s.Type == TypeFrontiere.Riviere || s.Type == TypeFrontiere.RiviereProlongee) continue;
                bool troue = false;
                foreach (int e in s.Aretes)
                {
                    if (!carte.AreteBloquante[e]) { troue = true; break; }
                }
                if (!troue) continue;

                s.Type = TypeFrontiere.RiviereProlongee;
                foreach (int e in s.Aretes)
                {
                    carte.TypeArete[e] = TypeFrontiere.RiviereProlongee;
                    carte.AreteBloquante[e] = true;
                }
            }

            // Balayage de sûreté sur TOUTES les arêtes inter-groupes, et pas seulement sur
            // celles des segments.
            //
            // Une frontière entre deux groupes peut se présenter en plusieurs morceaux — une
            // baie s'intercale — et le chaînage n'en retient que le plus long, pour y loger le
            // passage. Les morceaux écartés restaient sans type, donc franchissables : une
            // carte sur deux cents avait ainsi un couloir invisible entre deux groupes, que
            // seule la validation finale attrapait.
            var dePassage = new HashSet<int>();
            foreach (Passage passage in carte.Passages)
            {
                foreach (int e in passage.AretesFines) dePassage.Add(e);
            }

            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;
                if (carte.ZoneDeCellule[a] < 0 || carte.ZoneDeCellule[b] < 0) continue;
                if (carte.GroupeDeZone[carte.ZoneDeCellule[a]] == carte.GroupeDeZone[carte.ZoneDeCellule[b]]) continue;
                if (dePassage.Contains(e)) continue;
                if (carte.AreteBloquante[e]) continue;

                carte.TypeArete[e] = TypeFrontiere.RiviereProlongee;
                carte.AreteBloquante[e] = true;
                diag.NbAretesOrphelines++;
            }

            foreach (SegmentFrontiere s in segments)
            {
                foreach (int e in s.Aretes)
                {
                    if (!carte.AreteBloquante[e]) diag.NbAretesNonBloquantes++;
                }
            }
        }

        // -------------------------------------------------------------- réparation

        /// <summary>
        /// Rend praticables juste assez de cellules de massif pour que chaque zone et chaque
        /// groupe redeviennent d'un seul tenant.
        ///
        /// Empêcher les coupures à la pose ne suffit pas : deux segments voisins peuvent
        /// enfermer une poche sans qu'aucun des deux, pris isolément, ne dépasse son quota.
        /// On préfère toujours libérer une cellule qui touche deux morceaux à la fois — elle
        /// les recolle d'un coup — plutôt que de grignoter la bande au hasard.
        /// </summary>
        static int ReparerPraticabilite(Carte carte)
        {
            int liberees = 0;

            for (int z = 0; z < carte.NbZones; z++)
            {
                int zone = z;
                liberees += ReparerEnsemble(carte, carte.CellulesDeZone[z], c => carte.ZoneDeCellule[c] == zone);
            }

            for (int gr = 0; gr < carte.NbGroupes; gr++)
            {
                var cellules = new List<int>();
                foreach (int z in carte.ZonesDeGroupe[gr]) cellules.AddRange(carte.CellulesDeZone[z]);
                int groupe = gr;
                liberees += ReparerEnsemble(carte, cellules,
                    c => carte.ZoneDeCellule[c] >= 0 && carte.GroupeDeZone[carte.ZoneDeCellule[c]] == groupe);
            }
            return liberees;
        }

        static int ReparerEnsemble(Carte carte, List<int> cellules, Func<int, bool> dedans)
        {
            GrapheCellules g = carte.Graphe;
            int liberees = 0;

            for (int tentative = 0; tentative < 40; tentative++)
            {
                List<List<int>> composantes = Composantes(carte, cellules, dedans);

                // Zone entièrement engloutie : on lui rend une cellule, sans quoi elle
                // n'aurait aucun emplacement constructible.
                if (composantes.Count == 0)
                {
                    int rendue = PremiereLiberable(carte, cellules);
                    if (rendue < 0) break;
                    Liberer(carte, rendue);
                    liberees++;
                    continue;
                }
                if (composantes.Count == 1) break;

                int plusPetite = 0;
                for (int i = 1; i < composantes.Count; i++)
                {
                    if (composantes[i].Count < composantes[plusPetite].Count) plusPetite = i;
                }

                // Numéro de composante par cellule, pour repérer les cellules charnières.
                var numero = new Dictionary<int, int>();
                for (int i = 0; i < composantes.Count; i++)
                {
                    foreach (int c in composantes[i]) numero[c] = i;
                }

                int charniere = -1, secours = -1;
                foreach (int c in composantes[plusPetite])
                {
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || !dedans(v) || carte.CelluleReservee[v]) continue;
                        // Un lac isole aussi bien qu'un massif : il faut pouvoir l'entamer,
                        // faute de quoi une poche cernée par l'eau reste inaccessible.
                        if (!A(carte, v, DrapeauxCellule.Massif | DrapeauxCellule.Lac)) continue;

                        if (secours < 0 || v < secours) secours = v;

                        // Cette cellule touche-t-elle aussi une autre composante ?
                        for (int t = g.DebutCoins[v]; t < g.DebutCoins[v + 1]; t++)
                        {
                            int w = g.VoisinsDeCellule[t];
                            if (w < 0 || !numero.TryGetValue(w, out int comp) || comp == plusPetite) continue;
                            if (charniere < 0 || v < charniere) charniere = v;
                            break;
                        }
                    }
                }

                int liberer = charniere >= 0 ? charniere : secours;
                if (liberer < 0) break;

                Liberer(carte, liberer);
                liberees++;
            }
            return liberees;
        }

        static void Liberer(Carte carte, int cellule)
        {
            const ushort aRetirer = (ushort)(DrapeauxCellule.Massif | DrapeauxCellule.Lac
                                             | DrapeauxCellule.NonConstructible);
            carte.Drapeaux[cellule] &= unchecked((ushort)~aRetirer);
        }

        static int PremiereLiberable(Carte carte, List<int> cellules)
        {
            int meilleure = -1;
            foreach (int c in cellules)
            {
                if (!carte.Terre[c] || carte.CelluleReservee[c]) continue;
                if (!A(carte, c, DrapeauxCellule.NonConstructible)) continue;
                if (meilleure < 0 || c < meilleure) meilleure = c;
            }
            return meilleure;
        }

        /// <summary>
        /// Réduit chaque lac aux cellules qui portent encore le drapeau, et retire ceux que la
        /// réparation a entièrement vidés.
        /// </summary>
        static void NettoyerLacs(Carte carte)
        {
            var conserves = new List<LacData>(carte.Lacs.Count);
            foreach (LacData lac in carte.Lacs)
            {
                var restantes = new List<int>(lac.Cellules.Length);
                foreach (int c in lac.Cellules)
                {
                    if (A(carte, c, DrapeauxCellule.Lac)) restantes.Add(c);
                }
                if (restantes.Count == 0) continue;
                lac.Cellules = restantes.ToArray();
                conserves.Add(lac);
            }
            carte.Lacs.Clear();
            carte.Lacs.AddRange(conserves);
        }

        static List<List<int>> Composantes(Carte carte, List<int> cellules, Func<int, bool> dedans)
        {
            GrapheCellules g = carte.Graphe;
            var resultat = new List<List<int>>();
            var vu = new HashSet<int>();

            foreach (int depart in cellules)
            {
                if (vu.Contains(depart)) continue;
                if (!carte.Terre[depart] || A(carte, depart, DrapeauxCellule.NonConstructible)) continue;

                var composante = new List<int>();
                var pile = new Stack<int>();
                pile.Push(depart);
                vu.Add(depart);

                while (pile.Count > 0)
                {
                    int c = pile.Pop();
                    composante.Add(c);
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || vu.Contains(v) || !dedans(v)) continue;
                        if (!carte.Terre[v] || A(carte, v, DrapeauxCellule.NonConstructible)) continue;
                        vu.Add(v);
                        pile.Push(v);
                    }
                }
                resultat.Add(composante);
            }
            return resultat;
        }

        // ------------------------------------------------------------- praticabilité

        /// <summary>
        /// Une zone dont les cellules praticables seraient en deux morceaux serait
        /// injouable : on ne pourrait pas circuler d'un bâtiment à l'autre sans sortir. Même
        /// exigence à l'échelle du groupe.
        /// </summary>
        static void VerifierPraticabilite(Carte carte, DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;

            bool Praticable(int c) => carte.Terre[c] && !A(carte, c, DrapeauxCellule.NonConstructible);

            for (int z = 0; z < carte.NbZones; z++)
            {
                if (!UneSeuleComposante(g, carte.CellulesDeZone[z], Praticable,
                                        c => carte.ZoneDeCellule[c] == z)) diag.NbZonesCoupees++;
            }

            for (int gr = 0; gr < carte.NbGroupes; gr++)
            {
                var cellules = new List<int>();
                foreach (int z in carte.ZonesDeGroupe[gr]) cellules.AddRange(carte.CellulesDeZone[z]);
                int groupe = gr;
                // La mer porte la zone -1 : il faut l'écarter avant d'indexer les groupes.
                if (!UneSeuleComposante(g, cellules, Praticable,
                                        c => carte.ZoneDeCellule[c] >= 0
                                          && carte.GroupeDeZone[carte.ZoneDeCellule[c]] == groupe))
                {
                    diag.NbGroupesCoupes++;
                }
            }
        }

        static bool UneSeuleComposante(GrapheCellules g, List<int> cellules, Func<int, bool> praticable,
                                       Func<int, bool> dansLeGroupe)
        {
            int depart = -1, total = 0;
            foreach (int c in cellules)
            {
                if (!praticable(c)) continue;
                total++;
                if (depart < 0) depart = c;
            }
            if (total == 0) return false;

            var vu = new HashSet<int> { depart };
            var pile = new Stack<int>();
            pile.Push(depart);
            int atteintes = 0;

            while (pile.Count > 0)
            {
                int c = pile.Pop();
                atteintes++;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || vu.Contains(v) || !dansLeGroupe(v) || !praticable(v)) continue;
                    vu.Add(v);
                    pile.Push(v);
                }
            }
            return atteintes == total;
        }

        // ---------------------------------------------------------------- pente

        /// <summary>
        /// Surélève les massifs puis rabote ce qui reste trop raide. Le relief de base
        /// présentait déjà des faces à 70° sur des arêtes courtes ; sans cette passe, le
        /// navmesh y verrait des falaises là où l'on attend des collines.
        /// </summary>
        static void LimiterPente(Carte carte, ParametresMaterialisation p, DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;

            // Sommets de massif : hauteur partagée entre cellules massif voisines, pour que
            // la crête soit continue au lieu d'être une file d'aiguilles.
            var bonus = new float[g.NbCoins];
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (!A(carte, c, DrapeauxCellule.Massif)) continue;
                int voisinsMassif = 0;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v >= 0 && A(carte, v, DrapeauxCellule.Massif)) voisinsMassif++;
                }
                float hauteur = p.HauteurMassif * (0.55f + 0.15f * math.min(voisinsMassif, 3));
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int coin = g.CoinsDeCellule[s];
                    bonus[coin] = math.max(bonus[coin], hauteur);
                }
            }
            for (int coin = 0; coin < g.NbCoins; coin++) carte.HauteurCoin[coin] += bonus[coin];

            // Pieds de massif : cellules constructibles touchant un massif.
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (A(carte, c, DrapeauxCellule.Massif) || !carte.Terre[c]) continue;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v >= 0 && A(carte, v, DrapeauxCellule.Massif)) { Marquer(carte, c, DrapeauxCellule.PiedMassif); break; }
                }
            }

            // Rabotage : on abaisse toujours le point haut, jamais on ne remonte un lit.
            float penteMax = math.tan(math.radians(p.PenteMaxDegres));
            float penteMassif = math.tan(math.radians(p.PenteMassifMaxDegres));

            // Abaisser un sommet trop haut peut en rendre un autre trop raide à son tour :
            // trois passes ne suffisaient pas toujours à converger, et une carte sur douze
            // gardait une face au-delà de la limite.
            for (int passe = 0; passe < 8; passe++)
            {
                for (int e = 0; e < g.NbAretes; e++)
                {
                    int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                    if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;

                    bool massif = A(carte, a, DrapeauxCellule.Massif) || A(carte, b, DrapeauxCellule.Massif);
                    float limite = (massif ? penteMassif : penteMax) * math.max(g.LongueurArete[e], 1f);

                    int coinA = g.AreteCoinA[e], coinB = g.AreteCoinB[e];
                    float delta = carte.HauteurCoin[coinA] - carte.HauteurCoin[coinB];
                    if (math.abs(delta) <= limite) continue;

                    if (delta > 0) carte.HauteurCoin[coinA] = carte.HauteurCoin[coinB] + limite;
                    else carte.HauteurCoin[coinB] = carte.HauteurCoin[coinA] + limite;
                }
            }

            float pire = 0f;
            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;
                if (A(carte, a, DrapeauxCellule.Massif) || A(carte, b, DrapeauxCellule.Massif)) continue;
                float d = math.abs(carte.HauteurCoin[g.AreteCoinA[e]] - carte.HauteurCoin[g.AreteCoinB[e]]);
                float degres = math.degrees(math.atan(d / math.max(g.LongueurArete[e], 1f)));
                pire = math.max(pire, degres);
            }
            diag.PenteMaxHorsMassif = pire;

            // Quantification finale : après elle, MapData et maillage 3D coïncident exactement.
            for (int coin = 0; coin < g.NbCoins; coin++)
            {
                carte.HauteurCoin[coin] = (float)Math.Floor(carte.HauteurCoin[coin] * 20.0 + 0.5) / 20f;
            }
        }

        // -------------------------------------------------------------------- sockets

        static void PoserSockets(Carte carte, ParametresMaterialisation p, List<SegmentFrontiere> segments,
                                 DiagnosticMaterialisation diag)
        {
            GrapheCellules g = carte.Graphe;

            // Les mines se posent d'après les cellules de massif réellement présentes, et non
            // d'après le type des segments : la réparation peut vider entièrement une bande,
            // et le segment est alors retypé en cours d'eau. S'y fier laissait des cartes
            // sans le moindre gisement de minerai.
            // Une chaîne de montagnes borde plusieurs zones, mais une seule d'entre elles
            // porte le terrain Montagne : celle qui en contient le plus. Marquer toutes les
            // zones qui touchent un massif en donnait seize sur quatre-vingt-dix, là où la
            // conception en vise six à dix ; les autres gardent leurs cellules non
            // constructibles sans devenir pour autant des zones minières.
            var cellulesMassifParZone = new int[carte.NbZones];
            for (int c = 0; c < carte.NbCellules; c++)
            {
                if (!A(carte, c, DrapeauxCellule.Massif)) continue;
                int z = carte.ZoneDeCellule[c];
                if (z >= 0) cellulesMassifParZone[z]++;
            }

            var classement = new List<int>();
            for (int z = 0; z < carte.NbZones; z++)
            {
                if (cellulesMassifParZone[z] > 0) classement.Add(z);
            }
            classement.Sort((a, b) =>
            {
                if (cellulesMassifParZone[a] != cellulesMassifParZone[b])
                {
                    return cellulesMassifParZone[b] - cellulesMassifParZone[a];
                }
                return a - b;
            });

            carte.ZoneMontagne = new bool[carte.NbZones];
            int retenues = Math.Min(classement.Count, p.NbMassifsMax);
            for (int i = 0; i < retenues; i++) carte.ZoneMontagne[classement[i]] = true;

            for (int zoneMontagne = 0; zoneMontagne < carte.NbZones; zoneMontagne++)
            {
                if (!carte.ZoneMontagne[zoneMontagne]) continue;

                var candidats = new List<int>();
                foreach (int c in carte.CellulesDeZone[zoneMontagne])
                {
                    if (A(carte, c, DrapeauxCellule.NonConstructible)) continue;
                    if (!A(carte, c, DrapeauxCellule.PiedMassif)) continue;
                    candidats.Add(c);
                }

                int poses = 0;
                var placees = new List<int>();
                foreach (int c in candidats)
                {
                    if (poses >= p.SocketsMinesMaxParMassif) break;
                    bool tropProche = false;
                    foreach (int autre in placees)
                    {
                        if (math.distance(g.Sites[c], g.Sites[autre]) < p.DistanceMinSockets) { tropProche = true; break; }
                    }
                    if (tropProche) continue;
                    Marquer(carte, c, DrapeauxCellule.SocketMine);
                    placees.Add(c);
                    poses++;
                }

                // Au moins un socket par massif : sans lui, la zone Montagne serait un terrain
                // à minerai incapable d'en produire. Si aucun pied de massif n'est
                // constructible — la réparation a pu libérer toute la bande —, n'importe
                // quelle cellule bâtissable de la zone fera l'affaire.
                if (poses == 0)
                {
                    int repli = candidats.Count > 0 ? candidats[0] : -1;
                    if (repli < 0)
                    {
                        foreach (int c in carte.CellulesDeZone[zoneMontagne])
                        {
                            if (A(carte, c, DrapeauxCellule.NonConstructible)) continue;
                            repli = c;
                            break;
                        }
                    }
                    if (repli >= 0)
                    {
                        Marquer(carte, repli, DrapeauxCellule.SocketMine);
                        poses = 1;
                    }
                }
                diag.NbSocketsMines += poses;
            }

            for (int z = 0; z < carte.NbZones; z++)
            {
                int poses = 0;
                var placees = new List<int>();
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (poses >= p.SocketsPecheMaxParZone) break;
                    if (A(carte, c, DrapeauxCellule.NonConstructible)) continue;
                    if (!ProcheDeLEau(carte, c)) continue;

                    bool tropProche = false;
                    foreach (int autre in placees)
                    {
                        if (math.distance(g.Sites[c], g.Sites[autre]) < p.DistanceMinSockets) { tropProche = true; break; }
                    }
                    if (tropProche) continue;
                    Marquer(carte, c, DrapeauxCellule.SocketPeche);
                    placees.Add(c);
                    poses++;
                }
                diag.NbSocketsPeche += poses;
            }
        }

        /// <summary>
        /// Eau poissonneuse : la mer, un lac, ou une rivière que l'hydrologie a réellement
        /// tracée. Les rivières prolongées en sont exclues à dessein — ce sont des ruisseaux
        /// de séparation, et les compter donnerait du poisson aux quatre-vingt-dix zones,
        /// ce qui reviendrait à n'en donner à aucune.
        /// </summary>
        static bool ProcheDeLEau(Carte carte, int cellule)
        {
            GrapheCellules g = carte.Graphe;
            for (int s = g.DebutCoins[cellule]; s < g.DebutCoins[cellule + 1]; s++)
            {
                int v = g.VoisinsDeCellule[s];
                if (v < 0 || !carte.Terre[v]) return true;                       // mer
                if (A(carte, v, DrapeauxCellule.Lac)) return true;               // lac
                if (carte.TypeArete[g.AretesDeCellule[s]] == TypeFrontiere.Riviere) return true;
            }
            return false;
        }

        static void MarquerRiveraines(Carte carte, DiagnosticMaterialisation diag)
        {
            carte.ZoneRiveraine = new bool[carte.NbZones];
            for (int z = 0; z < carte.NbZones; z++)
            {
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (!ProcheDeLEau(carte, c)) continue;
                    carte.ZoneRiveraine[z] = true;
                    diag.NbZonesRiveraines++;
                    break;
                }
            }
        }
    }

    /// <summary>Plan d'eau intérieur.</summary>
    public sealed class LacData
    {
        public int[] Cellules;
        public float Niveau;
    }
}
