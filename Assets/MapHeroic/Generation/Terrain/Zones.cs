using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresZones
    {
        public int NbZones = 90;

        /// <summary>Cible interne, plus stricte que la règle, pour garder de la marge.</summary>
        public float ToleranceInterne = 0.25f;

        /// <summary>Tolérance imposée par la règle : ± 30 % autour de la médiane.</summary>
        public float ToleranceRegle = 0.30f;

        /// <summary>
        /// Périmètre² / aire maximal : au-delà, la zone est un serpent.
        ///
        /// Calibré sur la mesure, non sur l'intuition. Un disque parfait vaut 4π ≈ 12,6 et un
        /// carré 16, mais nos zones sont des agrégats d'une vingtaine de cellules de Voronoï
        /// dont le contour est dentelé : le périmètre suit les arêtes des cellules, bien plus
        /// long qu'un tracé lisse. La moyenne relevée est de 29. La valeur de 28 initialement
        /// retenue passait donc SOUS la moyenne et refusait des transferts parfaitement sains,
        /// ce qui laissait deux ou trois zones bloquées hors tolérance.
        /// </summary>
        public float CompaciteMax = 45f;

        public int CellulesMinParZone = 4;
        public int IterationsEquilibrage = 800;

        /// <summary>
        /// Passes de relaxation des germes : on fait croître, on replace chaque germe au
        /// centre de sa zone, on recommence. L'échantillonnage du point le plus éloigné
        /// place beaucoup de germes sur les pointes de l'île, où ils se retrouvent vite
        /// cernés par la mer et restent petits ; la relaxation les ramène vers le centre de
        /// masse et fait tomber l'écart de croissance de ≈ 50 % à ≈ 25 %, ce que
        /// l'équilibrage seul ne rattrapait pas.
        /// </summary>
        public int IterationsLloyd = 2;

        /// <summary>
        /// Candidats de transfert examinés par tentative. Ils sont triés du plus doux au plus
        /// dur ; au-delà des premiers, on ne trouve plus que des franchissements coûteux.
        /// </summary>
        public int CandidatsParTentative = 24;

        /// <summary>
        /// Zones examinées par itération, en partant des extrêmes.
        ///
        /// Balayer les quatre-vingt-dix rendait certaines cartes très lentes : quand presque
        /// aucun transfert n'est possible, chaque itération parcourait toutes les zones et
        /// tous leurs candidats avant d'en trouver un, et une graine sur deux cents montait à
        /// près de huit secondes. Les zones qui ont besoin d'un transfert sont de toute façon
        /// aux extrémités du classement.
        /// </summary>
        public int ZonesExamineesParIteration = 16;

        /// <summary>Itérations sans progrès de l'objectif avant d'arrêter.</summary>
        public int SansProgresMax = 60;

        /// <summary>
        /// Poids de la crête et de la dureté dans le coût de croissance, exprimés en
        /// « nombre de cellules d'avance » concédées pour éviter un franchissement.
        ///
        /// Ces coûts s'AJOUTENT à l'aire dans la clé de priorité au lieu de la multiplier.
        /// La forme multiplicative, essayée d'abord, fabrique mécaniquement le déséquilibre
        /// qu'on cherche à éviter : avec un facteur allant jusqu'à 4, une zone cernée de
        /// terrain dur attend d'être quatre fois plus petite qu'une autre avant d'avancer,
        /// et l'on mesurait 2,98 de rapport entre la plus grande et la plus petite. Sous
        /// forme additive, le relief décale la priorité de quelques cellules — assez pour
        /// que les frontières l'épousent, trop peu pour creuser des écarts d'aire.
        /// </summary>
        public float PoidsCrete = 3f;

        /// <inheritdoc cref="PoidsCrete"/>
        public float PoidsDurete = 6f;

        /// <summary>
        /// Au-delà de cette dureté, un transfert d'équilibrage est jugé « dur » : on ne s'y
        /// résout que si aucun transfert doux n'existe. C'est une contrainte molle et non un
        /// interdit, sans quoi un bassin dont l'aire n'est pas un multiple de l'aire de zone
        /// bloquerait l'équilibrage pour de bon.
        /// </summary>
        public float DureteMolle = 0.6f;

        public int RejeuxMax = 5;
    }

    public sealed class DiagnosticZones
    {
        public int Essais;
        public bool Reussi;
        public string MotifEchec;
        public int NbZones;
        public float AireMin, AireMediane, AireMax;
        public float EcartMax;                 // écart relatif maximal à la médiane
        public int NbHorsToleranceInterne;
        public int NbTransferts;
        public int IterationsEquilibrage;

        /// <summary>Écart maximal à la médiane avant équilibrage : mesure la qualité de la croissance seule.</summary>
        public float EcartMaxAvantEquilibrage;
        public int DegreMin, DegreMax;
        public int NbArticulationsGenantes;
        public float CompaciteMoyenne;
        public long Millisecondes;

        public override string ToString()
        {
            if (!Reussi)
            {
                return $"zones ÉCHEC après {Essais} essai(s) : {MotifEchec} " +
                       $"[écart {EcartMaxAvantEquilibrage:P1} → {EcartMax:P1}, " +
                       $"aires {AireMin:F0} / {AireMediane:F0} / {AireMax:F0}, " +
                       $"{NbTransferts} transferts en {IterationsEquilibrage} itérations, " +
                       $"{NbHorsToleranceInterne} zones hors ± 25 %]";
            }
            return $"{NbZones} zones en {Essais} essai(s) : aires {AireMin:F0} / {AireMediane:F0} / {AireMax:F0} m² " +
                   $"(écart max {EcartMaxAvantEquilibrage:P1} → {EcartMax:P1}, {NbHorsToleranceInterne} hors ± 25 %), " +
                   $"{NbTransferts} transferts en {IterationsEquilibrage} itérations, " +
                   $"degrés {DegreMin}-{DegreMax}, compacité moyenne {CompaciteMoyenne:F1}, {Millisecondes} ms";
        }
    }

    /// <summary>
    /// Phase P5 : découpe des terres en exactement 90 zones connexes d'aires comparables.
    ///
    /// Le compte exact est obtenu par construction et non par ajustement : on part de 90
    /// germes et on fait croître les zones jusqu'à épuisement des cellules. Aucune cellule ne
    /// reste orpheline, aucune zone ne peut disparaître — il y en aura 90, toujours.
    ///
    /// La croissance suit une file de priorité dont la clé est l'aire de la zone candidate,
    /// majorée par le coût du terrain à franchir. Deux conséquences : la plus petite zone
    /// avance en premier, ce qui égalise les aires au fil de l'eau ; et franchir une crête ou
    /// une rivière coûte cher, si bien que les frontières s'arrêtent d'elles-mêmes sur le
    /// relief au lieu d'être plaquées dessus après coup.
    ///
    /// L'équilibrage qui suit ne fait que corriger le reliquat, par transferts de cellules de
    /// bord, sous trois garde-fous : ne jamais couper une zone en deux, ne jamais la vider,
    /// ne jamais l'étirer en serpent.
    /// </summary>
    public static class Zones
    {
        public const int Phase = 6;

        public static bool Construire(Carte carte, ParametresZones p, Rng rngRacine, out DiagnosticZones diag)
        {
            if (carte?.Durete == null) throw new InvalidOperationException("L'hydrologie doit précéder les zones.");

            var chrono = System.Diagnostics.Stopwatch.StartNew();
            diag = new DiagnosticZones();
            GrapheCellules g = carte.Graphe;

            for (int essai = 0; essai < p.RejeuxMax; essai++)
            {
                diag = new DiagnosticZones { Essais = essai + 1 };
                var rng = rngRacine.Deriver(Phase * 100 + essai);

                int[] germes = ChoisirGermes(g, carte.Terre, p.NbZones, ref rng);
                if (germes == null)
                {
                    diag.MotifEchec = "pas assez de cellules de terre pour 90 germes";
                    break;
                }

                int[] zoneDe = FaireCroitre(g, carte, p, germes);
                for (int lloyd = 0; lloyd < p.IterationsLloyd; lloyd++)
                {
                    germes = RecentrerGermes(g, zoneDe, p.NbZones, germes);
                    zoneDe = FaireCroitre(g, carte, p, germes);
                }

                var cellulesDeZone = RegrouperParZone(zoneDe, p.NbZones);

                MesurerAires(g, cellulesDeZone, diag, p);
                diag.EcartMaxAvantEquilibrage = diag.EcartMax;

                Equilibrer(g, carte, p, zoneDe, cellulesDeZone, diag);

                MesurerAires(g, cellulesDeZone, diag, p);
                MesurerCompacite(g, cellulesDeZone, zoneDe, diag);

                if (diag.EcartMax > p.ToleranceRegle)
                {
                    diag.MotifEchec = $"aire hors ± {p.ToleranceRegle:P0} (écart max {diag.EcartMax:P1})";
                    continue;
                }

                var voisines = ConstruireGrapheZones(g, zoneDe, p.NbZones);
                MesurerDegres(voisines, diag);

                if (diag.DegreMin < 1)
                {
                    diag.MotifEchec = "une zone n'a aucune voisine";
                    continue;
                }

                diag.NbArticulationsGenantes = CompterArticulationsGenantes(voisines, p.NbZones, 3);
                if (diag.NbArticulationsGenantes > 0)
                {
                    diag.MotifEchec = $"{diag.NbArticulationsGenantes} zone(s) d'articulation isolant moins de 3 zones";
                    continue;
                }

                carte.AretesEntreZones = ConstruireFrontieres(g, carte, zoneDe, p.NbZones, out int[][] aretesDeZone);
                carte.AretesDeZone = aretesDeZone;
                carte.ZoneDeCellule = zoneDe;
                carte.CellulesDeZone = cellulesDeZone;
                carte.ZonesVoisines = voisines;
                diag.NbZones = p.NbZones;
                diag.Reussi = true;
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return true;
            }

            diag.Reussi = false;
            diag.Millisecondes = chrono.ElapsedMilliseconds;
            return false;
        }

        // ------------------------------------------------------------------- germes

        /// <summary>
        /// Germes par échantillonnage du point le plus éloigné : le premier au hasard, chaque
        /// suivant le plus loin de tous les précédents. Cela répartit les zones sans grille et
        /// sans paquets, ce qu'un simple tirage uniforme ne ferait pas.
        /// </summary>
        static int[] ChoisirGermes(GrapheCellules g, bool[] terre, int nb, ref Rng rng)
        {
            var terres = new List<int>(g.NbCellules);
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (terre[c]) terres.Add(c);
            }
            if (terres.Count < nb * 2) return null;

            var germes = new int[nb];
            var distMin = new float[g.NbCellules];
            for (int i = 0; i < distMin.Length; i++) distMin[i] = float.MaxValue;

            germes[0] = terres[rng.Entier(terres.Count)];
            Actualiser(germes[0]);

            for (int k = 1; k < nb; k++)
            {
                int meilleur = -1;
                float meilleure = -1f;
                for (int i = 0; i < terres.Count; i++)
                {
                    int c = terres[i];
                    float d = distMin[c];
                    if (d > meilleure) { meilleure = d; meilleur = c; }
                }
                germes[k] = meilleur;
                Actualiser(meilleur);
            }
            return germes;

            void Actualiser(int germe)
            {
                float2 pg = g.Sites[germe];
                for (int i = 0; i < terres.Count; i++)
                {
                    int c = terres[i];
                    float dx = g.Sites[c].x - pg.x;
                    float dy = g.Sites[c].y - pg.y;
                    float d = dx * dx + dy * dy;
                    if (d < distMin[c]) distMin[c] = d;
                }
            }
        }

        // ---------------------------------------------------------------- croissance

        static int[] FaireCroitre(GrapheCellules g, Carte carte, ParametresZones p, int[] germes)
        {
            var zoneDe = new int[g.NbCellules];
            for (int i = 0; i < zoneDe.Length; i++) zoneDe[i] = -1;

            var aire = new float[p.NbZones];
            var file = new FilePrioriteMin(4096);

            // Aire de référence : le coût du relief s'exprime en multiples de la cellule
            // médiane, ce qui rend les poids indépendants de l'échelle de la carte.
            float aireCellule = AireCelluleMediane(g, carte.Terre);

            // Une entrée par candidature (zone, cellule) : l'index d'entrée départage les
            // égalités de clé, ce qui rend l'ordre de dépilage total et reproductible.
            var entZone = new List<int>(g.NbCellules * 4);
            var entCellule = new List<int>(g.NbCellules * 4);
            var entDurete = new List<float>(g.NbCellules * 4);

            for (int z = 0; z < p.NbZones; z++)
            {
                int germe = germes[z];
                zoneDe[germe] = z;
                aire[z] = g.Aire[germe];
            }
            for (int z = 0; z < p.NbZones; z++) PousserVoisins(z, germes[z]);

            while (file.Depiler(out float cle, out int entree))
            {
                int z = entZone[entree];
                int c = entCellule[entree];
                if (zoneDe[c] != -1) continue;

                float cleActuelle = Cle(z, c, entDurete[entree]);
                // L'aire de la zone a pu grossir depuis l'empilement : si la clé a beaucoup
                // vieilli, on remet la candidature en file au lieu de l'honorer à tort.
                if (cleActuelle > cle * 1.10f)
                {
                    file.Empiler(cleActuelle, entree);
                    continue;
                }

                zoneDe[c] = z;
                aire[z] += g.Aire[c];
                PousserVoisins(z, c);
            }

            return zoneDe;

            float Cle(int zone, int cellule, float durete)
            {
                return aire[zone] + aireCellule * (p.PoidsCrete * carte.Crete[cellule] + p.PoidsDurete * durete);
            }

            void PousserVoisins(int zone, int depuis)
            {
                for (int s = g.DebutCoins[depuis]; s < g.DebutCoins[depuis + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || !carte.Terre[v] || zoneDe[v] != -1) continue;

                    float durete = carte.Durete[g.AretesDeCellule[s]];
                    entZone.Add(zone);
                    entCellule.Add(v);
                    entDurete.Add(durete);
                    file.Empiler(Cle(zone, v, durete), entZone.Count - 1);
                }
            }
        }

        /// <summary>
        /// Replace chaque germe sur la cellule de sa zone la plus proche du centre de masse.
        /// Le germe reste forcément dans sa zone : le prendre ailleurs ferait repartir la
        /// croissance d'une cellule déjà revendiquée.
        /// </summary>
        static int[] RecentrerGermes(GrapheCellules g, int[] zoneDe, int nbZones, int[] germesPrecedents)
        {
            var sommeX = new double[nbZones];
            var sommeY = new double[nbZones];
            var poids = new double[nbZones];

            for (int c = 0; c < g.NbCellules; c++)
            {
                int z = zoneDe[c];
                if (z < 0) continue;
                double a = g.Aire[c];
                sommeX[z] += g.Sites[c].x * a;
                sommeY[z] += g.Sites[c].y * a;
                poids[z] += a;
            }

            var germes = new int[nbZones];
            var meilleureDistance = new double[nbZones];
            for (int z = 0; z < nbZones; z++)
            {
                germes[z] = -1;
                meilleureDistance[z] = double.MaxValue;
            }

            for (int c = 0; c < g.NbCellules; c++)
            {
                int z = zoneDe[c];
                if (z < 0 || poids[z] <= 0.0) continue;

                double cx = sommeX[z] / poids[z];
                double cy = sommeY[z] / poids[z];
                double dx = g.Sites[c].x - cx;
                double dy = g.Sites[c].y - cy;
                double d = dx * dx + dy * dy;

                // Départage par index : deux cellules à égale distance du centre doivent
                // toujours donner le même germe d'une exécution à l'autre.
                if (d < meilleureDistance[z] || (d == meilleureDistance[z] && c < germes[z]))
                {
                    meilleureDistance[z] = d;
                    germes[z] = c;
                }
            }

            // Une zone vide — impossible après croissance, mais on ne laisse pas de trou.
            for (int z = 0; z < nbZones; z++)
            {
                if (germes[z] < 0) germes[z] = germesPrecedents[z];
            }
            return germes;
        }

        static float AireCelluleMediane(GrapheCellules g, bool[] terre)
        {
            var aires = new List<float>(g.NbCellules);
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (terre[c]) aires.Add(g.Aire[c]);
            }
            if (aires.Count == 0) return 1f;
            var tri = aires.ToArray();
            Array.Sort(tri);
            return tri[tri.Length / 2];
        }

        static List<int>[] RegrouperParZone(int[] zoneDe, int nbZones)
        {
            var listes = new List<int>[nbZones];
            for (int z = 0; z < nbZones; z++) listes[z] = new List<int>(32);
            for (int c = 0; c < zoneDe.Length; c++)
            {
                if (zoneDe[c] >= 0) listes[zoneDe[c]].Add(c);
            }
            return listes;
        }

        // -------------------------------------------------------------- équilibrage

        static void Equilibrer(GrapheCellules g, Carte carte, ParametresZones p,
                               int[] zoneDe, List<int>[] cellulesDeZone, DiagnosticZones diag)
        {
            var tampons = new Tampons(g.NbCellules);
            var aire = new float[p.NbZones];
            for (int z = 0; z < p.NbZones; z++)
            {
                foreach (int c in cellulesDeZone[z]) aire[z] += g.Aire[c];
            }

            var tri = new float[p.NbZones];
            var ordre = new int[p.NbZones];
            float meilleurObjectif = float.MaxValue;
            int sansProgres = 0;

            for (int iteration = 0; iteration < p.IterationsEquilibrage; iteration++)
            {
                diag.IterationsEquilibrage = iteration + 1;

                Array.Copy(aire, tri, p.NbZones);
                Array.Sort(tri);
                float mediane = tri[p.NbZones / 2];
                float plafond = mediane * (1f + p.ToleranceInterne);
                float plancher = mediane * (1f - p.ToleranceInterne);

                for (int z = 0; z < p.NbZones; z++) ordre[z] = z;

                // Toutes les zones en excès sont essayées, de la plus grosse à la plus petite,
                // et non la seule plus grande : celle-ci peut très bien n'avoir que des
                // voisines encore plus grosses, sans que cela empêche les autres de se
                // rééquilibrer. C'est en s'arrêtant au premier refus que l'équilibrage
                // laissait passer des écarts de 60 %.
                Array.Sort(ordre, (a, b) =>
                {
                    if (aire[a] != aire[b]) return aire[b] < aire[a] ? -1 : 1;
                    return a < b ? -1 : (a > b ? 1 : 0);
                });

                bool horsTolerance = false;
                for (int z = 0; z < p.NbZones; z++)
                {
                    if (aire[z] > plafond || aire[z] < plancher) { horsTolerance = true; break; }
                }
                if (!horsTolerance) break;

                // On n'essaie pas que les zones hors tolérance : une zone déjà rentrée dans
                // les clous peut avoir à transmettre à sa voisine la masse qu'elle vient de
                // recevoir. Le critère du carré garantit qu'aucun de ces transferts
                // intermédiaires n'aggrave l'équilibre d'ensemble.
                int examinees = math.min(p.ZonesExamineesParIteration, p.NbZones);
                bool fait = false;
                for (int i = 0; i < examinees && !fait; i++)
                {
                    fait = TenterDon(g, carte, p, zoneDe, cellulesDeZone, aire, ordre[i], mediane, tampons);
                }
                for (int i = 0; i < examinees && !fait; i++)
                {
                    int z = ordre[p.NbZones - 1 - i];
                    fait = TenterReception(g, carte, p, zoneDe, cellulesDeZone, aire, z, mediane, tampons);
                }

                if (!fait) break;                   // plus aucun transfert admissible
                diag.NbTransferts++;

                // Arrêt sur stagnation : la somme des écarts au carré ne peut que décroître,
                // mais elle peut décroître si peu que continuer ne sert plus à rien.
                float objectif = 0f;
                for (int z = 0; z < p.NbZones; z++)
                {
                    float ecart = aire[z] - mediane;
                    objectif += ecart * ecart;
                }
                if (objectif < meilleurObjectif * 0.999f)
                {
                    meilleurObjectif = objectif;
                    sansProgres = 0;
                }
                else if (++sansProgres > p.SansProgresMax) break;
            }
        }

        /// <summary>La zone la plus grande cède une cellule de bord à une voisine plus petite.</summary>
        static bool TenterDon(GrapheCellules g, Carte carte, ParametresZones p, int[] zoneDe,
                              List<int>[] cellulesDeZone, float[] aire, int donneuse, float mediane, Tampons tampons)
        {
            var candidats = new List<(int cellule, int vers, float durete)>();

            foreach (int c in cellulesDeZone[donneuse])
            {
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || !carte.Terre[v]) continue;
                    int autre = zoneDe[v];
                    if (autre < 0 || autre == donneuse) continue;
                    if (aire[autre] >= aire[donneuse]) continue;
                    candidats.Add((c, autre, carte.Durete[g.AretesDeCellule[s]]));
                }
            }

            return TenterTransferts(g, p, zoneDe, cellulesDeZone, aire, donneuse, mediane, candidats, tampons);
        }

        /// <summary>La zone la plus petite prend une cellule de bord à une voisine plus grande.</summary>
        static bool TenterReception(GrapheCellules g, Carte carte, ParametresZones p, int[] zoneDe,
                                    List<int>[] cellulesDeZone, float[] aire, int receveuse, float mediane, Tampons tampons)
        {
            var candidats = new List<(int cellule, int vers, float durete)>();

            foreach (int c in cellulesDeZone[receveuse])
            {
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || !carte.Terre[v]) continue;
                    int autre = zoneDe[v];
                    if (autre < 0 || autre == receveuse) continue;
                    if (aire[autre] <= aire[receveuse]) continue;
                    candidats.Add((v, receveuse, carte.Durete[g.AretesDeCellule[s]]));
                }
            }

            // La donneuse n'est pas imposée ici : c'est la voisine plus grande, quelle qu'elle soit.
            return TenterTransferts(g, p, zoneDe, cellulesDeZone, aire, -1, mediane, candidats, tampons);
        }

        static bool TenterTransferts(GrapheCellules g, ParametresZones p, int[] zoneDe,
                                     List<int>[] cellulesDeZone, float[] aire, int donneuseForcee,
                                     float mediane, List<(int cellule, int vers, float durete)> candidats, Tampons tampons)
        {
            // Transferts doux d'abord, par dureté croissante : on ne franchit une crête ou une
            // rivière que faute de mieux.
            candidats.Sort((a, b) =>
            {
                bool douxA = a.durete <= p.DureteMolle;
                bool douxB = b.durete <= p.DureteMolle;
                if (douxA != douxB) return douxA ? -1 : 1;
                if (a.durete != b.durete) return a.durete < b.durete ? -1 : 1;
                if (a.cellule != b.cellule) return a.cellule < b.cellule ? -1 : 1;
                return a.vers < b.vers ? -1 : (a.vers > b.vers ? 1 : 0);
            });

            int examines = 0;
            foreach (var (cellule, vers, _) in candidats)
            {
                if (++examines > p.CandidatsParTentative) break;
                int depuis = zoneDe[cellule];
                if (depuis < 0 || depuis == vers) continue;
                if (donneuseForcee >= 0 && depuis != donneuseForcee) continue;
                if (cellulesDeZone[depuis].Count <= p.CellulesMinParZone) continue;

                // Le transfert doit réduire la somme des écarts au CARRÉ. Deux raisons de
                // prendre le carré et non la valeur absolue. D'abord il interdit les
                // aller-retours, qui donnaient 1200 transferts pour 0,4 point gagné. Ensuite
                // il autorise le déplacement de proche en proche : céder une cellule d'une
                // zone à + 58 % vers une voisine à + 30 % laisse la somme des écarts absolus
                // inchangée, donc bloquée, alors qu'elle fait bien baisser la somme des
                // carrés. C'est indispensable ici, car une zone trop grande n'a souvent pour
                // voisines que d'autres zones trop grandes : la masse doit transiter.
                float aireCellule = g.Aire[cellule];
                float ecartDepuis = aire[depuis] - mediane;
                float ecartVers = aire[vers] - mediane;
                float apresDepuis = ecartDepuis - aireCellule;
                float apresVers = ecartVers + aireCellule;
                float avant = ecartDepuis * ecartDepuis + ecartVers * ecartVers;
                float apres = apresDepuis * apresDepuis + apresVers * apresVers;
                if (apres >= avant) continue;
                if (EstArticulationDeZone(g, zoneDe, cellulesDeZone[depuis], depuis, cellule, tampons)) continue;

                // Contrôle de compacité relatif et non absolu : une zone déjà trop étirée
                // resterait sinon bloquée pour toujours, puisque céder une cellule ne la
                // ramène pas d'un coup sous le seuil. C'est ce qui empêchait la plus grande
                // zone de se dégonfler. On exige donc : sous le seuil, ou pas pire qu'avant.
                if (!CompaciteAcceptable(g, cellulesDeZone[depuis], zoneDe, depuis, cellule, false, p.CompaciteMax)) continue;
                if (!CompaciteAcceptable(g, cellulesDeZone[vers], zoneDe, vers, cellule, true, p.CompaciteMax)) continue;

                cellulesDeZone[depuis].Remove(cellule);
                cellulesDeZone[vers].Add(cellule);
                zoneDe[cellule] = vers;
                aire[depuis] -= g.Aire[cellule];
                aire[vers] += g.Aire[cellule];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Tampons réutilisés par l'équilibrage. Le parcours de connexité est appelé des
        /// milliers de fois : allouer un ensemble et une pile à chaque appel coûtait plus
        /// cher que le parcours lui-même. Le marquage par numéro de passe évite en plus
        /// d'avoir à réinitialiser le tableau.
        /// </summary>
        sealed class Tampons
        {
            public int[] Marque;
            public int[] File;
            public int Passe;

            public Tampons(int nbCellules)
            {
                Marque = new int[nbCellules];
                File = new int[nbCellules];
            }
        }

        /// <summary>La zone reste-t-elle d'un seul tenant si on lui retire cette cellule ?</summary>
        static bool EstArticulationDeZone(GrapheCellules g, int[] zoneDe, List<int> cellules,
                                          int zone, int retiree, Tampons tampons)
        {
            int depart = -1;
            foreach (int c in cellules)
            {
                if (c != retiree) { depart = c; break; }
            }
            if (depart < 0) return true;

            int passe = ++tampons.Passe;
            int[] marque = tampons.Marque;
            int[] file = tampons.File;

            int tete = 0, queue = 0;
            file[queue++] = depart;
            marque[depart] = passe;
            int atteintes = 0;

            while (tete < queue)
            {
                int c = file[tete++];
                atteintes++;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || v == retiree || zoneDe[v] != zone || marque[v] == passe) continue;
                    marque[v] = passe;
                    file[queue++] = v;
                }
            }
            return atteintes != cellules.Count - 1;
        }

        static bool CompaciteAcceptable(GrapheCellules g, List<int> cellules, int[] zoneDe, int zone,
                                        int cellule, bool ajout, float maximum)
        {
            float apres = Compacite(g, cellules, zoneDe, zone, cellule, ajout);
            if (apres <= maximum) return true;
            float avant = Compacite(g, cellules, zoneDe, zone, -1, false);
            return apres <= avant;
        }

        /// <summary>
        /// Périmètre² / aire, en simulant l'ajout ou le retrait d'une cellule.
        /// Passer -1 en <c>celluleModifiee</c> mesure l'état courant.
        /// </summary>
        static float Compacite(GrapheCellules g, List<int> cellules, int[] zoneDe, int zone,
                               int celluleModifiee, bool ajout)
        {
            double aire = 0.0, perimetre = 0.0;

            void Traiter(int c)
            {
                aire += g.Aire[c];
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    bool dedans;
                    if (v < 0) dedans = false;
                    else if (v == celluleModifiee) dedans = ajout;
                    else dedans = zoneDe[v] == zone;
                    if (!dedans) perimetre += g.LongueurArete[g.AretesDeCellule[s]];
                }
            }

            foreach (int c in cellules)
            {
                if (!ajout && c == celluleModifiee) continue;
                Traiter(c);
            }
            if (ajout && celluleModifiee >= 0) Traiter(celluleModifiee);

            if (aire <= 0.0) return float.MaxValue;
            return (float)(perimetre * perimetre / aire);
        }

        // ------------------------------------------------------------------ mesures

        static void MesurerAires(GrapheCellules g, List<int>[] cellulesDeZone, DiagnosticZones diag, ParametresZones p)
        {
            int nb = cellulesDeZone.Length;
            var aires = new float[nb];
            for (int z = 0; z < nb; z++)
            {
                foreach (int c in cellulesDeZone[z]) aires[z] += g.Aire[c];
            }

            var tri = (float[])aires.Clone();
            Array.Sort(tri);
            float mediane = tri[nb / 2];

            diag.AireMin = tri[0];
            diag.AireMax = tri[nb - 1];
            diag.AireMediane = mediane;

            float ecartMax = 0f;
            int hors = 0;
            for (int z = 0; z < nb; z++)
            {
                float ecart = math.abs(aires[z] - mediane) / mediane;
                if (ecart > ecartMax) ecartMax = ecart;
                if (ecart > p.ToleranceInterne) hors++;
            }
            diag.EcartMax = ecartMax;
            diag.NbHorsToleranceInterne = hors;
        }

        static void MesurerCompacite(GrapheCellules g, List<int>[] cellulesDeZone, int[] zoneDe, DiagnosticZones diag)
        {
            double somme = 0.0;
            int nb = cellulesDeZone.Length;
            for (int z = 0; z < nb; z++)
            {
                double aire = 0.0, perimetre = 0.0;
                foreach (int c in cellulesDeZone[z])
                {
                    aire += g.Aire[c];
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || zoneDe[v] != z) perimetre += g.LongueurArete[g.AretesDeCellule[s]];
                    }
                }
                if (aire > 0.0) somme += perimetre * perimetre / aire;
            }
            diag.CompaciteMoyenne = (float)(somme / nb);
        }

        static void MesurerDegres(List<int>[] voisines, DiagnosticZones diag)
        {
            diag.DegreMin = int.MaxValue;
            diag.DegreMax = 0;
            foreach (List<int> v in voisines)
            {
                if (v.Count < diag.DegreMin) diag.DegreMin = v.Count;
                if (v.Count > diag.DegreMax) diag.DegreMax = v.Count;
            }
            if (diag.DegreMin == int.MaxValue) diag.DegreMin = 0;
        }

        // ------------------------------------------------------- graphe des zones

        public static List<int>[] ConstruireGrapheZones(GrapheCellules g, int[] zoneDe, int nbZones)
        {
            var voisines = new List<int>[nbZones];
            for (int z = 0; z < nbZones; z++) voisines[z] = new List<int>(8);

            var vues = new HashSet<long>();
            for (int c = 0; c < g.NbCellules; c++)
            {
                int za = zoneDe[c];
                if (za < 0) continue;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0) continue;
                    int zb = zoneDe[v];
                    if (zb < 0 || zb == za) continue;

                    long cle = za < zb ? ((long)za << 32) | (uint)zb : ((long)zb << 32) | (uint)za;
                    if (!vues.Add(cle)) continue;
                    voisines[za].Add(zb);
                    voisines[zb].Add(za);
                }
            }

            for (int z = 0; z < nbZones; z++) voisines[z].Sort();
            return voisines;
        }

        /// <summary>
        /// Agrège les arêtes fines en frontières entre zones. Les phases suivantes ne
        /// raisonnent plus qu'à ce niveau : c'est cent fois moins d'objets à parcourir, et
        /// les grandeurs qui comptent (longueur, dureté, présence d'une rivière) y sont
        /// déjà calculées.
        /// </summary>
        public static AreteZones[] ConstruireFrontieres(GrapheCellules g, Carte carte, int[] zoneDe,
                                                        int nbZones, out int[][] aretesDeZone)
        {
            var parPaire = new Dictionary<long, List<int>>();

            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e];
                int b = g.AreteCelluleB[e];
                if (b < 0) continue;
                int za = zoneDe[a], zb = zoneDe[b];
                if (za < 0 || zb < 0 || za == zb) continue;

                long cle = za < zb ? ((long)za << 32) | (uint)zb : ((long)zb << 32) | (uint)za;
                if (!parPaire.TryGetValue(cle, out List<int> liste))
                {
                    liste = new List<int>(8);
                    parPaire[cle] = liste;
                }
                liste.Add(e);
            }

            // Les clés sont triées avant construction : l'itération d'un dictionnaire n'a pas
            // d'ordre garanti, et la numérotation des frontières doit être reproductible.
            var cles = new long[parPaire.Count];
            parPaire.Keys.CopyTo(cles, 0);
            Array.Sort(cles);

            var frontieres = new AreteZones[cles.Length];
            var parZone = new List<int>[nbZones];
            for (int z = 0; z < nbZones; z++) parZone[z] = new List<int>(8);

            for (int i = 0; i < cles.Length; i++)
            {
                long cle = cles[i];
                int za = (int)(cle >> 32);
                int zb = (int)(cle & 0xFFFFFFFF);
                List<int> aretes = parPaire[cle];
                aretes.Sort();

                double longueur = 0.0, durete = 0.0, crete = 0.0, riviere = 0.0;
                foreach (int e in aretes)
                {
                    float l = g.LongueurArete[e];
                    longueur += l;
                    durete += l * carte.Durete[e];
                    crete += l * 0.5f * (carte.Crete[g.AreteCelluleA[e]] + carte.Crete[g.AreteCelluleB[e]]);
                    if (carte.AreteRiviere[e]) riviere += l;
                }

                frontieres[i] = new AreteZones
                {
                    ZoneA = za,
                    ZoneB = zb,
                    AretesFines = aretes.ToArray(),
                    Longueur = (float)longueur,
                    Durete = longueur > 0.0 ? (float)(durete / longueur) : 0f,
                    Crete = longueur > 0.0 ? (float)(crete / longueur) : 0f,
                    LongueurRiviere = (float)riviere
                };
                parZone[za].Add(i);
                parZone[zb].Add(i);
            }

            aretesDeZone = new int[nbZones][];
            for (int z = 0; z < nbZones; z++) aretesDeZone[z] = parZone[z].ToArray();
            return frontieres;
        }

        /// <summary>
        /// Nombre de zones dont le retrait isolerait un morceau de moins de <c>seuil</c>
        /// zones. Un tel morceau ne pourrait former aucun groupe de 3 ou 4 sans passer par
        /// cette zone unique : autant le savoir maintenant.
        /// </summary>
        public static int CompterArticulationsGenantes(List<int>[] voisines, int nbZones, int seuil)
        {
            var decouverte = new int[nbZones];
            var basse = new int[nbZones];
            var parent = new int[nbZones];
            var taille = new int[nbZones];
            for (int i = 0; i < nbZones; i++) { decouverte[i] = -1; parent[i] = -1; taille[i] = 1; }

            var pileNoeud = new int[nbZones + 1];
            var pileIndex = new int[nbZones + 1];
            int temps = 0;
            int genantes = 0;

            for (int racine = 0; racine < nbZones; racine++)
            {
                if (decouverte[racine] != -1) continue;

                int sommet = 0;
                pileNoeud[0] = racine;
                pileIndex[0] = 0;
                decouverte[racine] = basse[racine] = temps++;
                var enfantsRacine = new List<int>();

                while (sommet >= 0)
                {
                    int u = pileNoeud[sommet];
                    if (pileIndex[sommet] < voisines[u].Count)
                    {
                        int v = voisines[u][pileIndex[sommet]++];
                        if (v == parent[u]) continue;

                        if (decouverte[v] != -1)
                        {
                            if (decouverte[v] < basse[u]) basse[u] = decouverte[v];
                        }
                        else
                        {
                            parent[v] = u;
                            decouverte[v] = basse[v] = temps++;
                            sommet++;
                            pileNoeud[sommet] = v;
                            pileIndex[sommet] = 0;
                        }
                    }
                    else
                    {
                        sommet--;
                        if (sommet < 0) break;
                        int pere = pileNoeud[sommet];
                        taille[pere] += taille[u];
                        if (basse[u] < basse[pere]) basse[pere] = basse[u];

                        if (pere == racine) enfantsRacine.Add(u);
                        else if (basse[u] >= decouverte[pere] && taille[u] < seuil) genantes++;
                    }
                }

                if (enfantsRacine.Count > 1)
                {
                    foreach (int enfant in enfantsRacine)
                    {
                        if (taille[enfant] < seuil) genantes++;
                    }
                }
            }
            return genantes;
        }
    }
}
