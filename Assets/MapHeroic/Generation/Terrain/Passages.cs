using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    /// <summary>Frontière entre deux groupes voisins, avec ses arêtes fines mises bout à bout.</summary>
    public sealed class FrontiereGroupes
    {
        public int GroupeA;
        public int GroupeB;

        /// <summary>Arêtes fines de la plus longue chaîne continue, dans l'ordre du tracé.</summary>
        public int[] Chaine;

        /// <summary>Coins de la chaîne, un de plus que le nombre d'arêtes.</summary>
        public int[] Coins;

        /// <summary>Longueur de la chaîne, en mètres.</summary>
        public float Longueur;

        public float Durete;

        /// <summary>
        /// Plus large ouverture réalisable : longueur maximale d'une suite de 2 à 4 arêtes
        /// consécutives de la chaîne.
        /// </summary>
        public float LargeurRealisable;

        /// <summary>Assez longue pour qu'un col puisse s'y loger.</summary>
        public bool Eligible;

        /// <summary>Index du passage qui la traverse, ou -1.</summary>
        public int Passage = -1;
    }

    /// <summary>Ouverture praticable entre deux groupes.</summary>
    public sealed class Passage
    {
        public int Id;
        public int GroupeA;
        public int GroupeB;

        /// <summary>Arêtes fines consécutives que le passage franchit.</summary>
        public int[] AretesFines;

        /// <summary>Coins bordant ces arêtes.</summary>
        public int[] Coins;

        /// <summary>Cellules réservées : ni massif, ni lac, ni lit de rivière ne s'y posera.</summary>
        public int[] Cellules;

        /// <summary>Largeur réelle de l'ouverture, en mètres.</summary>
        public float Largeur;

        public float2 Position;
    }

    public sealed class ParametresPassages
    {
        /// <summary>
        /// Longueur minimale d'une frontière pour y loger un passage. En deçà, il n'y a
        /// physiquement pas la place pour un col plausible : la frontière reste bloquante.
        /// </summary>
        public float LongueurMinChaine = 45f;

        /// <summary>Largeur minimale d'un passage, en mètres.</summary>
        public float LargeurMin = 28f;

        /// <summary>Arêtes fines consécutives formant un passage.</summary>
        public int AretesMin = 2;
        public int AretesMax = 4;

        /// <summary>Distance minimale entre un passage et l'extrémité de sa frontière.</summary>
        public float MargeExtremite = 14f;

        public int DegreMax = 3;

        /// <summary>Probabilités des degrés cibles 1, 2 et 3.</summary>
        public float[] ProbabiliteDegre = { 0.25f, 0.45f, 0.30f };

        public int EssaisArbre = 50;
        public int RejeuxMax = 5;
    }

    public sealed class DiagnosticPassages
    {
        public int Essais;
        public bool Reussi;
        public string MotifEchec;
        public int NbFrontieres;
        public int NbFrontieresEligibles;
        public int NbPassages;
        public int DegreMin;
        public int DegreMax;
        public float LargeurMin;
        public float LargeurMoyenne;
        public int NbCellulesReservees;
        public bool GrapheConnexe;
        public long Millisecondes;

        public override string ToString()
        {
            if (!Reussi) return $"passages ÉCHEC après {Essais} essai(s) : {MotifEchec}";
            return $"{NbPassages} passages sur {NbFrontieresEligibles}/{NbFrontieres} frontières éligibles " +
                   $"en {Essais} essai(s) : degrés {DegreMin}-{DegreMax}, largeurs {LargeurMin:F0} à " +
                   $"{LargeurMoyenne:F0} m en moyenne, {NbCellulesReservees} cellules réservées, " +
                   $"graphe {(GrapheConnexe ? "connexe" : "COUPÉ")}, {Millisecondes} ms";
        }
    }

    /// <summary>
    /// Phase P7 : choix et localisation des passages entre groupes.
    ///
    /// Le graphe des groupes doit rester connexe — sinon des joueurs seraient enfermés — mais
    /// chaque groupe ne doit avoir qu'un à trois passages, pour que les goulets restent
    /// défendables. Ces deux exigences se satisfont ensemble par un arbre couvrant à degré
    /// borné : un arbre garantit la connexité avec le minimum d'arêtes, la borne de degré
    /// garantit qu'aucun groupe ne devienne un carrefour. On ajoute ensuite quelques arêtes
    /// hors arbre pour atteindre les degrés voulus, jamais au-delà de trois.
    ///
    /// Une frontière trop courte n'est pas éligible : il n'y a pas la place d'y loger un col
    /// plausible. Si les frontières éligibles ne suffisent plus à relier tous les groupes, ce
    /// n'est pas au placement des passages de se débrouiller — c'est le regroupement qui est
    /// à refaire, et la phase le signale en échouant.
    /// </summary>
    public static class Passages
    {
        public const int Phase = 8;

        public static bool Construire(Carte carte, ParametresPassages p, Rng rngRacine, out DiagnosticPassages diag)
        {
            if (carte?.GroupeDeZone == null) throw new InvalidOperationException("Les groupes doivent précéder les passages.");

            var chrono = System.Diagnostics.Stopwatch.StartNew();
            diag = new DiagnosticPassages();

            FrontiereGroupes[] frontieres = ConstruireFrontieres(carte, p);
            diag.NbFrontieres = frontieres.Length;
            foreach (FrontiereGroupes f in frontieres)
            {
                if (f.Eligible) diag.NbFrontieresEligibles++;
            }

            if (!SousGrapheEligibleConnexe(frontieres, carte.NbGroupes))
            {
                diag.MotifEchec = "les frontières assez longues pour un col ne relient pas tous les groupes";
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return false;
            }

            for (int essai = 0; essai < p.RejeuxMax; essai++)
            {
                diag.Essais = essai + 1;
                var rng = rngRacine.Deriver(Phase * 100 + essai);

                int[] retenues = ChoisirFrontieres(frontieres, carte.NbGroupes, p, ref rng);
                if (retenues == null)
                {
                    diag.MotifEchec = $"aucun arbre couvrant de degré ≤ {p.DegreMax} en {p.EssaisArbre} essais";
                    continue;
                }

                var passages = new List<Passage>(retenues.Length);
                var reservees = new bool[carte.NbCellules];
                bool echec = false;

                foreach (int index in retenues)
                {
                    Passage passage = Localiser(carte, frontieres[index], p, passages.Count);
                    if (passage == null) { echec = true; break; }
                    frontieres[index].Passage = passages.Count;
                    passages.Add(passage);
                    foreach (int c in passage.Cellules) reservees[c] = true;
                }

                if (echec)
                {
                    diag.MotifEchec = "une frontière retenue n'offre aucune ouverture assez large";
                    foreach (FrontiereGroupes f in frontieres) f.Passage = -1;
                    continue;
                }

                carte.FrontieresGroupes = frontieres;
                carte.Passages = passages;
                carte.CelluleReservee = reservees;

                Mesurer(carte, p, diag);
                diag.Reussi = true;
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return true;
            }

            diag.Reussi = false;
            diag.Millisecondes = chrono.ElapsedMilliseconds;
            return false;
        }

        // ------------------------------------------------------------------ frontières

        static FrontiereGroupes[] ConstruireFrontieres(Carte carte, ParametresPassages p)
        {
            GrapheCellules g = carte.Graphe;
            var parPaire = new Dictionary<long, List<int>>();

            foreach (AreteZones az in carte.AretesEntreZones)
            {
                int ga = carte.GroupeDeZone[az.ZoneA];
                int gb = carte.GroupeDeZone[az.ZoneB];
                if (ga == gb) continue;

                long cle = ga < gb ? ((long)ga << 32) | (uint)gb : ((long)gb << 32) | (uint)ga;
                if (!parPaire.TryGetValue(cle, out List<int> liste))
                {
                    liste = new List<int>(16);
                    parPaire[cle] = liste;
                }
                liste.AddRange(az.AretesFines);
            }

            var cles = new long[parPaire.Count];
            parPaire.Keys.CopyTo(cles, 0);
            Array.Sort(cles);

            var resultat = new FrontiereGroupes[cles.Length];
            for (int i = 0; i < cles.Length; i++)
            {
                long cle = cles[i];
                List<int> aretes = parPaire[cle];
                aretes.Sort();

                OrdonnerChaine(g, aretes, out int[] chaine, out int[] coins, out float longueur);

                double durete = 0.0;
                foreach (int e in chaine) durete += g.LongueurArete[e] * carte.Durete[e];

                float largeurRealisable = MeilleureFenetre(g, chaine, p);

                resultat[i] = new FrontiereGroupes
                {
                    GroupeA = (int)(cle >> 32),
                    GroupeB = (int)(cle & 0xFFFFFFFF),
                    Chaine = chaine,
                    Coins = coins,
                    Longueur = longueur,
                    Durete = longueur > 0f ? (float)(durete / longueur) : 0f,
                    LargeurRealisable = largeurRealisable,
                    // L'éligibilité teste ce dont le passage a réellement besoin, et pas
                    // seulement la longueur totale : une chaîne de 45 m faite de huit arêtes
                    // courtes n'offre aucune fenêtre de 28 m sur 2 à 4 arêtes. Sans ce test
                    // ici, l'échec ne se découvrait qu'au moment de placer le passage et
                    // faisait rejouer toute la phase.
                    Eligible = longueur >= p.LongueurMinChaine
                               && chaine.Length >= p.AretesMin
                               && largeurRealisable >= p.LargeurMin
                };
            }
            return resultat;
        }

        /// <summary>
        /// Longueur de la plus large suite de 2 à 4 arêtes consécutives : la meilleure
        /// ouverture que cette frontière puisse offrir.
        /// </summary>
        static float MeilleureFenetre(GrapheCellules g, int[] chaine, ParametresPassages p)
        {
            float meilleure = 0f;
            for (int nb = p.AretesMin; nb <= p.AretesMax && nb <= chaine.Length; nb++)
            {
                for (int debut = 0; debut + nb <= chaine.Length; debut++)
                {
                    float somme = 0f;
                    for (int k = 0; k < nb; k++) somme += g.LongueurArete[chaine[debut + k]];
                    if (somme > meilleure) meilleure = somme;
                }
            }
            return meilleure;
        }

        /// <summary>
        /// Met les arêtes bout à bout en suivant les coins partagés, et retient la plus longue
        /// chaîne continue. Une frontière entre deux groupes peut se présenter en plusieurs
        /// morceaux — une baie s'intercale, par exemple — et un passage doit tenir tout entier
        /// dans un seul d'entre eux.
        /// </summary>
        static void OrdonnerChaine(GrapheCellules g, List<int> aretes, out int[] chaine,
                                   out int[] coins, out float longueur)
        {
            var incidentes = new Dictionary<int, List<int>>();
            foreach (int e in aretes)
            {
                Ajouter(g.AreteCoinA[e], e);
                Ajouter(g.AreteCoinB[e], e);
            }

            void Ajouter(int coin, int arete)
            {
                if (!incidentes.TryGetValue(coin, out List<int> liste))
                {
                    liste = new List<int>(4);
                    incidentes[coin] = liste;
                }
                liste.Add(arete);
            }

            var vues = new HashSet<int>();
            int[] meilleureChaine = Array.Empty<int>();
            int[] meilleursCoins = Array.Empty<int>();
            float meilleureLongueur = 0f;

            foreach (int depart in aretes)
            {
                if (vues.Contains(depart)) continue;

                // Composante connexe de `depart`, puis marche depuis une extrémité.
                var composante = new List<int>();
                var pile = new Stack<int>();
                pile.Push(depart);
                vues.Add(depart);
                while (pile.Count > 0)
                {
                    int e = pile.Pop();
                    composante.Add(e);
                    foreach (int coin in new[] { g.AreteCoinA[e], g.AreteCoinB[e] })
                    {
                        foreach (int voisine in incidentes[coin])
                        {
                            if (vues.Contains(voisine)) continue;
                            vues.Add(voisine);
                            pile.Push(voisine);
                        }
                    }
                }

                var ensemble = new HashSet<int>(composante);
                int coinDepart = TrouverExtremite(g, composante, incidentes, ensemble);
                MarcherChaine(g, composante, incidentes, ensemble, coinDepart,
                              out int[] ordre, out int[] sommets, out float l);

                if (l > meilleureLongueur)
                {
                    meilleureLongueur = l;
                    meilleureChaine = ordre;
                    meilleursCoins = sommets;
                }
            }

            chaine = meilleureChaine;
            coins = meilleursCoins;
            longueur = meilleureLongueur;
        }

        static int TrouverExtremite(GrapheCellules g, List<int> composante,
                                    Dictionary<int, List<int>> incidentes, HashSet<int> ensemble)
        {
            int meilleur = -1;
            foreach (int e in composante)
            {
                foreach (int coin in new[] { g.AreteCoinA[e], g.AreteCoinB[e] })
                {
                    int degre = 0;
                    foreach (int voisine in incidentes[coin])
                    {
                        if (ensemble.Contains(voisine)) degre++;
                    }
                    if (degre == 1 && (meilleur < 0 || coin < meilleur)) meilleur = coin;
                }
            }
            // Chaîne fermée : n'importe quel coin fait un départ, on prend le plus petit index.
            if (meilleur < 0) meilleur = g.AreteCoinA[composante[0]];
            return meilleur;
        }

        static void MarcherChaine(GrapheCellules g, List<int> composante, Dictionary<int, List<int>> incidentes,
                                  HashSet<int> ensemble, int coinDepart,
                                  out int[] chaine, out int[] coins, out float longueur)
        {
            var ordre = new List<int>(composante.Count);
            var sommets = new List<int>(composante.Count + 1) { coinDepart };
            var utilisees = new HashSet<int>();
            int coinCourant = coinDepart;
            longueur = 0f;

            while (true)
            {
                int suivante = -1;
                foreach (int e in incidentes[coinCourant])
                {
                    if (!ensemble.Contains(e) || utilisees.Contains(e)) continue;
                    if (suivante < 0 || e < suivante) suivante = e;
                }
                if (suivante < 0) break;

                utilisees.Add(suivante);
                ordre.Add(suivante);
                longueur += g.LongueurArete[suivante];
                coinCourant = g.AreteCoinA[suivante] == coinCourant ? g.AreteCoinB[suivante] : g.AreteCoinA[suivante];
                sommets.Add(coinCourant);
            }

            chaine = ordre.ToArray();
            coins = sommets.ToArray();
        }

        // ------------------------------------------------------- choix des frontières

        static bool SousGrapheEligibleConnexe(FrontiereGroupes[] frontieres, int nbGroupes)
        {
            var uf = new UnionFind(nbGroupes);
            foreach (FrontiereGroupes f in frontieres)
            {
                if (f.Eligible) uf.Unir(f.GroupeA, f.GroupeB);
            }
            return uf.NbComposantes == 1;
        }

        /// <summary>
        /// Arbre couvrant de degré borné, puis quelques arêtes en plus pour atteindre les
        /// degrés cibles. Les poids sont tirés au hasard à chaque essai : c'est ce qui donne
        /// des réseaux de passages différents d'une graine à l'autre, là où un poids fixe
        /// produirait toujours la même ossature.
        /// </summary>
        static int[] ChoisirFrontieres(FrontiereGroupes[] frontieres, int nbGroupes,
                                       ParametresPassages p, ref Rng rng)
        {
            var eligibles = new List<int>();
            for (int i = 0; i < frontieres.Length; i++)
            {
                if (frontieres[i].Eligible) eligibles.Add(i);
            }

            var poids = new float[frontieres.Length];
            var ordre = new int[eligibles.Count];
            var degre = new int[nbGroupes];
            List<int> arbre = null;

            for (int essai = 0; essai < p.EssaisArbre; essai++)
            {
                foreach (int i in eligibles) poids[i] = rng.Float01();
                for (int k = 0; k < eligibles.Count; k++) ordre[k] = eligibles[k];
                Array.Sort(ordre, (a, b) =>
                {
                    if (poids[a] != poids[b]) return poids[a] < poids[b] ? -1 : 1;
                    return a < b ? -1 : (a > b ? 1 : 0);
                });

                var uf = new UnionFind(nbGroupes);
                Array.Clear(degre, 0, degre.Length);
                var retenues = new List<int>(nbGroupes);

                foreach (int i in ordre)
                {
                    FrontiereGroupes f = frontieres[i];
                    if (uf.Trouver(f.GroupeA) == uf.Trouver(f.GroupeB)) continue;
                    if (degre[f.GroupeA] >= p.DegreMax || degre[f.GroupeB] >= p.DegreMax) continue;
                    uf.Unir(f.GroupeA, f.GroupeB);
                    degre[f.GroupeA]++;
                    degre[f.GroupeB]++;
                    retenues.Add(i);
                }

                if (uf.NbComposantes == 1) { arbre = retenues; break; }
            }

            if (arbre == null) return null;

            // Degrés cibles, puis ajout d'arêtes hors arbre tant que les deux extrémités
            // restent sous leur cible. Jamais de relâchement au-delà de DegreMax.
            var cible = new int[nbGroupes];
            for (int gr = 0; gr < nbGroupes; gr++) cible[gr] = TirerDegre(p, ref rng);

            var dansArbre = new bool[frontieres.Length];
            foreach (int i in arbre) dansArbre[i] = true;

            // Les candidates hors arbre sont parcourues des frontières les plus dures aux
            // plus molles : à degré égal, autant ouvrir le passage là où le terrain le
            // justifie le mieux.
            var supplementaires = new List<int>(eligibles);
            supplementaires.RemoveAll(i => dansArbre[i]);
            supplementaires.Sort((a, b) =>
            {
                float da = frontieres[a].Durete, db = frontieres[b].Durete;
                if (da != db) return db < da ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            foreach (int i in supplementaires)
            {
                FrontiereGroupes f = frontieres[i];
                if (degre[f.GroupeA] >= Math.Min(cible[f.GroupeA], p.DegreMax)) continue;
                if (degre[f.GroupeB] >= Math.Min(cible[f.GroupeB], p.DegreMax)) continue;
                degre[f.GroupeA]++;
                degre[f.GroupeB]++;
                arbre.Add(i);
            }

            arbre.Sort();
            return arbre.ToArray();
        }

        static int TirerDegre(ParametresPassages p, ref Rng rng)
        {
            float t = rng.Float01();
            float cumul = 0f;
            for (int d = 0; d < p.ProbabiliteDegre.Length; d++)
            {
                cumul += p.ProbabiliteDegre[d];
                if (t < cumul) return d + 1;
            }
            return p.ProbabiliteDegre.Length;
        }

        // -------------------------------------------------------------- localisation

        /// <summary>
        /// Place le passage au point le plus bas et le plus sec de la frontière : là où un col
        /// ou un gué existerait naturellement. On s'écarte des extrémités, qui sont des
        /// jonctions entre trois groupes ou des arrivées à la mer.
        /// </summary>
        static Passage Localiser(Carte carte, FrontiereGroupes f, ParametresPassages p, int id)
        {
            GrapheCellules g = carte.Graphe;
            int nbAretes = f.Chaine.Length;
            if (nbAretes < p.AretesMin) return null;

            // Abscisse curviligne de chaque coin le long de la chaîne.
            var abscisse = new float[f.Coins.Length];
            for (int i = 1; i < f.Coins.Length; i++)
            {
                abscisse[i] = abscisse[i - 1] + g.LongueurArete[f.Chaine[i - 1]];
            }

            float fluxMax = 1f;
            foreach (int coin in f.Coins) fluxMax = math.max(fluxMax, carte.Flux[coin]);

            // On examine toutes les fenêtres assez larges et on garde la moins coûteuse, au
            // lieu de fixer d'abord le meilleur coin puis d'espérer qu'une fenêtre centrée
            // dessus soit assez large. L'ordre inverse laissait échouer des frontières qui
            // offraient pourtant une ouverture ailleurs.
            int debutArete = -1;
            int nbArete = 0;
            float largeur = 0f;
            float meilleurCout = float.MaxValue;

            for (int nb = p.AretesMin; nb <= p.AretesMax && nb <= nbAretes; nb++)
            {
                for (int debut = 0; debut + nb <= nbAretes; debut++)
                {
                    float somme = 0f;
                    for (int k = 0; k < nb; k++) somme += g.LongueurArete[f.Chaine[debut + k]];
                    if (somme < p.LargeurMin) continue;

                    int milieu = debut + nb / 2;
                    int coin = f.Coins[milieu];
                    float cout = CoutDeCoin(carte, g, coin, fluxMax);

                    // S'approcher d'une extrémité est pénalisé, pas interdit : une jonction
                    // de trois groupes ou une arrivée à la mer fait un col peu crédible, mais
                    // mieux vaut un passage mal placé que pas de passage du tout.
                    if (abscisse[milieu] < p.MargeExtremite ||
                        f.Longueur - abscisse[milieu] < p.MargeExtremite)
                    {
                        cout += 1f;
                    }

                    // À coût égal, la suite la plus courte : c'est un col, pas une plaine.
                    cout += 0.01f * nb;

                    if (cout < meilleurCout || (cout == meilleurCout && debut < debutArete))
                    {
                        meilleurCout = cout;
                        debutArete = debut;
                        nbArete = nb;
                        largeur = somme;
                    }
                }
            }

            if (debutArete < 0) return null;

            var aretes = new int[nbArete];
            Array.Copy(f.Chaine, debutArete, aretes, 0, aretes.Length);

            var coins = new int[aretes.Length + 1];
            Array.Copy(f.Coins, debutArete, coins, 0, coins.Length);

            var cellules = new SortedSet<int>();
            foreach (int e in aretes)
            {
                cellules.Add(g.AreteCelluleA[e]);
                if (g.AreteCelluleB[e] >= 0) cellules.Add(g.AreteCelluleB[e]);
            }
            foreach (int coin in coins)
            {
                for (int k = g.DebutCellulesDeCoin[coin]; k < g.DebutCellulesDeCoin[coin + 1]; k++)
                {
                    cellules.Add(g.CellulesDeCoin[k]);
                }
            }

            int coinCentral = coins[coins.Length / 2];
            var tableau = new int[cellules.Count];
            cellules.CopyTo(tableau);

            return new Passage
            {
                Id = id,
                GroupeA = f.GroupeA,
                GroupeB = f.GroupeB,
                AretesFines = aretes,
                Coins = coins,
                Cellules = tableau,
                Largeur = largeur,
                Position = g.Coins[coinCentral]
            };
        }

        /// <summary>
        /// Coût d'un coin comme emplacement de col : bas et sec vaut mieux que haut et
        /// ruisselant. Le flux est normalisé par le maximum de la frontière, de sorte que le
        /// terme reste comparable au champ de crêtes, lui déjà dans [0, 1].
        /// </summary>
        static float CoutDeCoin(Carte carte, GrapheCellules g, int coin, float fluxMax)
        {
            float crete = 0f;
            int deb = g.DebutCellulesDeCoin[coin];
            int fin = g.DebutCellulesDeCoin[coin + 1];
            for (int k = deb; k < fin; k++) crete += carte.Crete[g.CellulesDeCoin[k]];
            if (fin > deb) crete /= fin - deb;
            return crete + carte.Flux[coin] / fluxMax;
        }

        // ------------------------------------------------------------------- mesures

        static void Mesurer(Carte carte, ParametresPassages p, DiagnosticPassages diag)
        {
            diag.NbPassages = carte.Passages.Count;

            var degre = new int[carte.NbGroupes];
            double somme = 0.0;
            diag.LargeurMin = float.MaxValue;

            foreach (Passage passage in carte.Passages)
            {
                degre[passage.GroupeA]++;
                degre[passage.GroupeB]++;
                somme += passage.Largeur;
                if (passage.Largeur < diag.LargeurMin) diag.LargeurMin = passage.Largeur;
            }
            diag.LargeurMoyenne = carte.Passages.Count > 0 ? (float)(somme / carte.Passages.Count) : 0f;
            if (carte.Passages.Count == 0) diag.LargeurMin = 0f;

            diag.DegreMin = int.MaxValue;
            foreach (int d in degre)
            {
                if (d < diag.DegreMin) diag.DegreMin = d;
                if (d > diag.DegreMax) diag.DegreMax = d;
            }
            if (carte.NbGroupes == 0) diag.DegreMin = 0;

            for (int c = 0; c < carte.NbCellules; c++)
            {
                if (carte.CelluleReservee[c]) diag.NbCellulesReservees++;
            }

            var uf = new UnionFind(carte.NbGroupes);
            foreach (Passage passage in carte.Passages) uf.Unir(passage.GroupeA, passage.GroupeB);
            diag.GrapheConnexe = uf.NbComposantes == 1;
        }
    }

    /// <summary>Union-find avec compression de chemin, pour l'arbre couvrant.</summary>
    public sealed class UnionFind
    {
        readonly int[] _parent;
        readonly int[] _rang;

        public int NbComposantes { get; private set; }

        public UnionFind(int taille)
        {
            _parent = new int[taille];
            _rang = new int[taille];
            for (int i = 0; i < taille; i++) _parent[i] = i;
            NbComposantes = taille;
        }

        public int Trouver(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }
            return x;
        }

        public bool Unir(int a, int b)
        {
            int ra = Trouver(a), rb = Trouver(b);
            if (ra == rb) return false;
            if (_rang[ra] < _rang[rb]) { int t = ra; ra = rb; rb = t; }
            _parent[rb] = ra;
            if (_rang[ra] == _rang[rb]) _rang[ra]++;
            NbComposantes--;
            return true;
        }
    }
}
