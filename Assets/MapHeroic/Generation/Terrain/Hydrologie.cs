using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresHydrologie
    {
        /// <summary>Dénivelé imposé entre deux coins lors du comblement, en mètres.</summary>
        public float PasComblement = 0.02f;

        /// <summary>Élévation en deçà de laquelle un coin n'est pas jugé comblé.</summary>
        public float SeuilComblement = 0.05f;

        public int TailleBassinMin = 3;
        public int TailleBassinMax = 6;

        /// <summary>Nombre de rivières conservées, tiré dans cet intervalle inclusif.</summary>
        public int NbRivieresMin = 4;
        public int NbRivieresMax = 6;

        /// <summary>Flux minimal pour qu'un coin soit considéré comme un lit de rivière.</summary>
        public float SeuilFlux = 40f;
    }

    public sealed class DiagnosticHydrologie
    {
        public int NbCoinsCombles;
        public int NbBassinsCandidats;
        public int NbRivieres;
        public float FluxMax;
        public float LongueurMoyenneRiviere;
        public int NbCoinsSansEcoulement;
        public float DureteMoyenneTerre;

        public override string ToString()
        {
            return $"hydrologie : {NbRivieres} rivières (longueur moyenne {LongueurMoyenneRiviere:F0} coins, flux max {FluxMax:F0}), " +
                   $"{NbCoinsCombles} coins comblés, {NbBassinsCandidats} bassins candidats, " +
                   $"{NbCoinsSansEcoulement} coins sans écoulement, dureté moyenne {DureteMoyenneTerre:F2}";
        }
    }

    /// <summary>
    /// Phase P4, seconde moitié : comblement des dépressions, accumulation de flux,
    /// rivières, dureté des arêtes.
    ///
    /// L'ordre compte. On comble d'abord toute cuvette fermée en la remontant juste
    /// au-dessus de son exutoire : après cette passe, tout coin possède un chemin
    /// strictement descendant jusqu'à la mer, ce qui garantit que l'écoulement calculé
    /// ensuite ne tourne jamais en rond. Les cuvettes assez petites sont mémorisées : ce
    /// sont les futurs lacs.
    ///
    /// L'accumulation de flux se lit simplement : chaque coin s'écoule vers son voisin le
    /// plus bas et lui transmet tout ce qu'il a reçu. Traiter les coins du plus haut au plus
    /// bas suffit à tout propager en une passe, sans récursion.
    ///
    /// La dureté qui en sort est le lien avec la suite du pipeline : elle dira à la phase
    /// des zones où le terrain résiste déjà, pour que les frontières s'y posent d'elles-mêmes.
    /// </summary>
    public static class Hydrologie
    {
        /// <summary>
        /// Identifiant de sous-flux. P4 en compte deux, un par moitié : 4 pour le relief,
        /// 44 pour l'hydrologie. Ce n'est pas le numéro de phase du document, seulement une
        /// étiquette qui doit rester unique et stable.
        /// </summary>
        public const int Phase = 44;

        public static void Construire(Carte carte, ParametresHydrologie p, Rng rngRacine, out DiagnosticHydrologie diag)
        {
            if (carte?.HauteurCoin == null) throw new InvalidOperationException("Le relief doit précéder l'hydrologie.");

            GrapheCellules g = carte.Graphe;
            diag = new DiagnosticHydrologie();
            var rng = rngRacine.Deriver(Phase);

            bool[] exutoire = MarquerExutoires(g, carte.Terre);
            float[] elevation = ComblerDepressions(g, carte.HauteurCoin, exutoire, p, out float[] comble);
            carte.HauteurCoin = elevation;

            for (int i = 0; i < comble.Length; i++)
            {
                if (comble[i] > p.SeuilComblement) diag.NbCoinsCombles++;
            }

            carte.BassinsCandidats.Clear();
            carte.BassinsCandidats.AddRange(BassinsCandidats(g, carte, comble, p));
            diag.NbBassinsCandidats = carte.BassinsCandidats.Count;

            carte.Aval = CalculerAval(g, elevation, exutoire, out diag.NbCoinsSansEcoulement);
            carte.Flux = AccumulerFlux(g, elevation, carte.Aval, out diag.FluxMax);

            carte.CoinRiviere = new bool[g.NbCoins];
            carte.Rivieres.Clear();
            carte.Rivieres.AddRange(TracerRivieres(g, carte, exutoire, p, ref rng));
            diag.NbRivieres = carte.Rivieres.Count;

            long total = 0;
            foreach (int[] r in carte.Rivieres) total += r.Length;
            diag.LongueurMoyenneRiviere = carte.Rivieres.Count > 0 ? (float)total / carte.Rivieres.Count : 0f;

            carte.Durete = CalculerDurete(g, carte, out diag.DureteMoyenneTerre);
            carte.AreteRiviere = MarquerAretesRiviere(g, carte);
        }

        /// <summary>Un coin touchant une cellule de mer est un exutoire : l'eau y quitte l'île.</summary>
        static bool[] MarquerExutoires(GrapheCellules g, bool[] terre)
        {
            var exutoire = new bool[g.NbCoins];
            for (int coin = 0; coin < g.NbCoins; coin++)
            {
                int deb = g.DebutCellulesDeCoin[coin];
                int fin = g.DebutCellulesDeCoin[coin + 1];
                if (deb == fin) { exutoire[coin] = true; continue; }
                for (int i = deb; i < fin; i++)
                {
                    if (!terre[g.CellulesDeCoin[i]]) { exutoire[coin] = true; break; }
                }
            }
            return exutoire;
        }

        /// <summary>
        /// Comblement par inondation prioritaire : on part de la mer et on remonte vers
        /// l'intérieur en n'autorisant jamais un coin à être plus bas que celui par lequel on
        /// l'a atteint. Ce qui doit être relevé l'est du minimum nécessaire.
        /// </summary>
        static float[] ComblerDepressions(GrapheCellules g, float[] hauteurInitiale, bool[] exutoire,
                                          ParametresHydrologie p, out float[] comble)
        {
            var hauteur = (float[])hauteurInitiale.Clone();
            comble = new float[g.NbCoins];

            var traite = new bool[g.NbCoins];
            var file = new FilePrioriteMin(1024);

            for (int coin = 0; coin < g.NbCoins; coin++)
            {
                if (!exutoire[coin]) continue;
                traite[coin] = true;
                file.Empiler(hauteur[coin], coin);
            }

            while (file.Depiler(out float h, out int c))
            {
                for (int i = g.DebutCoinsVoisins[c]; i < g.DebutCoinsVoisins[c + 1]; i++)
                {
                    int v = g.CoinsVoisins[i];
                    if (traite[v]) continue;
                    traite[v] = true;

                    float minimum = h + p.PasComblement;
                    if (hauteur[v] < minimum)
                    {
                        comble[v] = minimum - hauteur[v];
                        hauteur[v] = minimum;
                    }
                    file.Empiler(hauteur[v], v);
                }
            }

            return hauteur;
        }

        /// <summary>
        /// Cellules dont tous les coins ont été relevés : ce sont des cuvettes. Regroupées,
        /// celles de la bonne taille deviendront des lacs.
        /// </summary>
        static List<int[]> BassinsCandidats(GrapheCellules g, Carte carte, float[] comble, ParametresHydrologie p)
        {
            var creuse = new bool[g.NbCellules];
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (!carte.Terre[c]) continue;
                bool tous = true;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    if (comble[g.CoinsDeCellule[s]] <= p.SeuilComblement) { tous = false; break; }
                }
                creuse[c] = tous;
            }

            var resultat = new List<int[]>();
            var vu = new bool[g.NbCellules];
            var file = new int[g.NbCellules];
            var membres = new List<int>();

            for (int depart = 0; depart < g.NbCellules; depart++)
            {
                if (!creuse[depart] || vu[depart]) continue;

                membres.Clear();
                int tete = 0, queue = 0;
                file[queue++] = depart;
                vu[depart] = true;

                while (tete < queue)
                {
                    int c = file[tete++];
                    membres.Add(c);
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || !creuse[v] || vu[v]) continue;
                        vu[v] = true;
                        file[queue++] = v;
                    }
                }

                if (membres.Count >= p.TailleBassinMin && membres.Count <= p.TailleBassinMax)
                {
                    resultat.Add(membres.ToArray());
                }
            }
            return resultat;
        }

        static int[] CalculerAval(GrapheCellules g, float[] hauteur, bool[] exutoire, out int nbSansEcoulement)
        {
            var aval = new int[g.NbCoins];
            nbSansEcoulement = 0;

            for (int c = 0; c < g.NbCoins; c++)
            {
                if (exutoire[c]) { aval[c] = -1; continue; }

                int meilleur = -1;
                for (int i = g.DebutCoinsVoisins[c]; i < g.DebutCoinsVoisins[c + 1]; i++)
                {
                    int v = g.CoinsVoisins[i];
                    if (hauteur[v] >= hauteur[c]) continue;
                    // Départage par index : deux voisins de même altitude doivent toujours
                    // donner le même choix d'une exécution à l'autre.
                    if (meilleur < 0
                        || hauteur[v] < hauteur[meilleur]
                        || (hauteur[v] == hauteur[meilleur] && v < meilleur))
                    {
                        meilleur = v;
                    }
                }

                aval[c] = meilleur;
                if (meilleur < 0) nbSansEcoulement++;
            }
            return aval;
        }

        static float[] AccumulerFlux(GrapheCellules g, float[] hauteur, int[] aval, out float fluxMax)
        {
            var flux = new float[g.NbCoins];
            for (int i = 0; i < flux.Length; i++) flux[i] = 1f;

            var ordre = new int[g.NbCoins];
            for (int i = 0; i < ordre.Length; i++) ordre[i] = i;
            Array.Sort(ordre, (a, b) =>
            {
                if (hauteur[a] != hauteur[b]) return hauteur[b] < hauteur[a] ? -1 : 1;  // décroissant
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            fluxMax = 0f;
            for (int i = 0; i < ordre.Length; i++)
            {
                int c = ordre[i];
                if (flux[c] > fluxMax) fluxMax = flux[c];
                int bas = aval[c];
                if (bas >= 0) flux[bas] += flux[c];
            }
            return flux;
        }

        /// <summary>
        /// Les rivières retenues sont celles dont l'embouchure draine le plus. On remonte
        /// depuis la mer en suivant à chaque fourche l'affluent le plus gros, jusqu'à passer
        /// sous le seuil de flux : la chaîne obtenue est le cours principal, et les affluents
        /// plus modestes restent de simples écoulements sans lit marqué.
        /// </summary>
        static List<int[]> TracerRivieres(GrapheCellules g, Carte carte, bool[] exutoire,
                                          ParametresHydrologie p, ref Rng rng)
        {
            int[] aval = carte.Aval;
            float[] flux = carte.Flux;

            // Amonts de chaque coin, en CSR.
            var degre = new int[g.NbCoins];
            for (int c = 0; c < g.NbCoins; c++)
            {
                if (aval[c] >= 0) degre[aval[c]]++;
            }
            var debutAmont = new int[g.NbCoins + 1];
            int cumul = 0;
            for (int c = 0; c < g.NbCoins; c++) { debutAmont[c] = cumul; cumul += degre[c]; }
            debutAmont[g.NbCoins] = cumul;

            var amonts = new int[cumul];
            var curseur = new int[g.NbCoins];
            for (int c = 0; c < g.NbCoins; c++) curseur[c] = debutAmont[c];
            for (int c = 0; c < g.NbCoins; c++)
            {
                if (aval[c] >= 0) amonts[curseur[aval[c]]++] = c;
            }

            // Embouchures : coins d'exutoire recevant un flux notable.
            var embouchures = new List<int>();
            for (int c = 0; c < g.NbCoins; c++)
            {
                if (!exutoire[c]) continue;
                if (flux[c] < p.SeuilFlux) continue;
                embouchures.Add(c);
            }
            embouchures.Sort((a, b) =>
            {
                if (flux[a] != flux[b]) return flux[b] < flux[a] ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            int voulu = rng.Entier(p.NbRivieresMin, p.NbRivieresMax + 1);
            int nb = Math.Min(voulu, embouchures.Count);

            var rivieres = new List<int[]>(nb);
            var chaine = new List<int>(64);

            for (int i = 0; i < nb; i++)
            {
                chaine.Clear();
                int c = embouchures[i];
                chaine.Add(c);

                // Deux rivières ne doivent pas partager de lit : on s'arrête si l'on rejoint
                // un cours déjà tracé.
                while (true)
                {
                    int meilleur = -1;
                    float meilleurFlux = p.SeuilFlux;
                    for (int k = debutAmont[c]; k < debutAmont[c + 1]; k++)
                    {
                        int amont = amonts[k];
                        if (carte.CoinRiviere[amont]) continue;
                        if (flux[amont] > meilleurFlux || (flux[amont] == meilleurFlux && meilleur >= 0 && amont < meilleur))
                        {
                            meilleur = amont;
                            meilleurFlux = flux[amont];
                        }
                    }
                    if (meilleur < 0) break;
                    c = meilleur;
                    chaine.Add(c);
                    if (chaine.Count > 512) break;
                }

                if (chaine.Count < 3) continue;

                foreach (int coin in chaine) carte.CoinRiviere[coin] = true;
                chaine.Reverse();                       // de la source vers l'embouchure
                rivieres.Add(chaine.ToArray());
            }

            return rivieres;
        }

        /// <summary>
        /// Arêtes fines traversées par un lit de rivière. La phase des groupes en a besoin
        /// séparément de la dureté : une rivière à l'intérieur d'un groupe est bien plus
        /// gênante qu'une simple crête, puisqu'elle coupera le groupe en deux.
        /// </summary>
        static bool[] MarquerAretesRiviere(GrapheCellules g, Carte carte)
        {
            var estRiviere = new bool[g.NbAretes];
            for (int e = 0; e < g.NbAretes; e++)
            {
                int coinA = g.AreteCoinA[e];
                int coinB = g.AreteCoinB[e];
                estRiviere[e] = carte.CoinRiviere[coinA] && carte.CoinRiviere[coinB]
                                && (carte.Aval[coinA] == coinB || carte.Aval[coinB] == coinA);
            }
            return estRiviere;
        }

        /// <summary>
        /// Dureté par arête : ce qui coûte cher à franchir. La mer et les lits de rivière
        /// valent 1, le reste hérite du champ de crêtes des deux cellules riveraines.
        /// </summary>
        static float[] CalculerDurete(GrapheCellules g, Carte carte, out float moyenneTerre)
        {
            var durete = new float[g.NbAretes];
            double somme = 0.0;
            int compte = 0;

            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e];
                int b = g.AreteCelluleB[e];

                bool bordDeMer = b < 0 || !carte.Terre[a] || !carte.Terre[b];
                if (bordDeMer) { durete[e] = 1f; continue; }

                int coinA = g.AreteCoinA[e];
                int coinB = g.AreteCoinB[e];
                bool litDeRiviere = carte.CoinRiviere[coinA] && carte.CoinRiviere[coinB]
                                    && (carte.Aval[coinA] == coinB || carte.Aval[coinB] == coinA);

                durete[e] = litDeRiviere ? 1f : 0.5f * (carte.Crete[a] + carte.Crete[b]);
                somme += durete[e];
                compte++;
            }

            moyenneTerre = compte > 0 ? (float)(somme / compte) : 0f;
            return durete;
        }
    }
}
