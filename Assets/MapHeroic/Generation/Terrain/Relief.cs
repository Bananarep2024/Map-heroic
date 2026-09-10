using System;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresRelief
    {
        /// <summary>Altitude vers laquelle tend l'intérieur des terres, en mètres.</summary>
        public float HauteurPlateau = 11f;

        /// <summary>Distance (en cellules) sur laquelle la côte remonte vers le plateau.</summary>
        public float EchelleCotiere = 9f;

        public float HauteurPlage = 0.4f;
        public float AmplitudeBruit = 1.5f;
        public float FrequenceBruit = 0.01f;

        /// <summary>
        /// Deux octaves, pas trois. La troisième aurait une longueur d'onde de 25 m, soit la
        /// taille d'une cellule : elle ne dessinerait rien de visible mais ferait osciller
        /// l'altitude d'un sommet à l'autre, gonflant les pentes sans contrepartie.
        /// </summary>
        public int OctavesBruit = 2;

        /// <summary>
        /// Hauteur ajoutée sur une crête pleine, en mètres. Modeste à dessein : ces crêtes
        /// ne sont pas les montagnes, seulement les lignes de relief qui indiqueront à P8 où
        /// les poser. Les massifs, eux, monteront à une vingtaine de mètres.
        /// </summary>
        public float AmplitudeCrete = 5f;

        public float FrequenceCrete = 0.006f;
        public int OctavesCrete = 3;

        /// <summary>
        /// Part des cellules de terre portant une crête. Les seuils du champ de crêtes sont
        /// déduits de cette cible par quantile, et non fixés à l'avance : la distribution du
        /// bruit fractionnaire dépend du nombre d'octaves et de la graine, de sorte que des
        /// seuils constants donneraient une couverture imprévisible.
        /// </summary>
        public float PartCretes = 0.25f;

        /// <summary>Part des cellules portant une crête d'intensité maximale.</summary>
        public float PartCretesFortes = 0.09f;

        /// <summary>Pente du fond marin, en mètres par cellule.</summary>
        public float PenteFondMarin = 1.2f;
    }

    public sealed class DiagnosticRelief
    {
        public float PartCellulesEnCrete;
        public float SeuilCreteBas;
        public float SeuilCreteHaut;
        public float HauteurMax;
        public float HauteurMin;
        public float HauteurMedianeTerre;
        public float PenteMoyenneDegres;
        public float PenteMaxDegres;

        public override string ToString()
        {
            return $"relief : crêtes sur {PartCellulesEnCrete:P1} des terres (seuils {SeuilCreteBas:F3}/{SeuilCreteHaut:F3}), " +
                   $"altitudes {HauteurMin:F1} à {HauteurMax:F1} m (médiane terre {HauteurMedianeTerre:F1}), " +
                   $"pente moyenne {PenteMoyenneDegres:F2}° / max {PenteMaxDegres:F1}°";
        }
    }

    /// <summary>
    /// Phase P4, première moitié : altitude de chaque coin et champ de crêtes.
    ///
    /// Trois termes se superposent. Une remontée exponentielle depuis le rivage donne la
    /// forme d'ensemble — plage, puis pente douce, puis plateau. Du bruit fractionnaire de
    /// faible amplitude évite que ce plateau soit lisse comme une table. Enfin un bruit
    /// « en crêtes » (1 − |bruit|) ajoute des lignes de hauteur continues : c'est lui qui
    /// produira des chaînes de montagnes plutôt que des bosses isolées, parce que ses maxima
    /// forment des lignes et non des taches.
    /// </summary>
    public static class Relief
    {
        public const int Phase = 4;

        public static void Construire(Carte carte, ParametresRelief p, Rng rngRacine, out DiagnosticRelief diag)
        {
            if (carte?.Graphe == null) throw new ArgumentNullException(nameof(carte));
            if (carte.Terre == null) throw new InvalidOperationException("La phase P3 doit précéder P4.");

            GrapheCellules g = carte.Graphe;
            diag = new DiagnosticRelief();

            var rng = rngRacine.Deriver(Phase);
            uint graineCrete = rng.Suivant();
            uint graineBruit = rng.Suivant();

            carte.Crete = ChampDeCretes(g, carte.Terre, p, graineCrete, diag);
            carte.HauteurCoin = Altitudes(g, carte, p, graineBruit);
            Mesurer(g, carte, diag);
        }

        /// <summary>
        /// Champ de crêtes calibré par quantile sur la part de cellules visée, puis adouci
        /// par un smoothstep entre les deux seuils.
        /// </summary>
        static float[] ChampDeCretes(GrapheCellules g, bool[] terre, ParametresRelief p,
                                     uint graine, DiagnosticRelief diag)
        {
            var brut = new float[g.NbCellules];
            int nbTerre = 0;
            for (int c = 0; c < g.NbCellules; c++)
            {
                brut[c] = Bruit.Crete(g.Sites[c], p.OctavesCrete, p.FrequenceCrete, graine);
                if (terre[c]) nbTerre++;
            }

            var echantillon = new float[Math.Max(1, nbTerre)];
            int k = 0;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (terre[c]) echantillon[k++] = brut[c];
            }
            Array.Sort(echantillon);

            float seuilBas = Quantile(echantillon, 1f - p.PartCretes);
            float seuilHaut = Quantile(echantillon, 1f - p.PartCretesFortes);
            if (seuilHaut <= seuilBas) seuilHaut = seuilBas + 1e-4f;

            diag.SeuilCreteBas = seuilBas;
            diag.SeuilCreteHaut = seuilHaut;

            var crete = new float[g.NbCellules];
            int enCrete = 0;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (!terre[c]) continue;
                crete[c] = Smoothstep(seuilBas, seuilHaut, brut[c]);
                if (crete[c] > 0f) enCrete++;
            }
            diag.PartCellulesEnCrete = nbTerre > 0 ? (float)enCrete / nbTerre : 0f;
            return crete;
        }

        static float[] Altitudes(GrapheCellules g, Carte carte, ParametresRelief p, uint graine)
        {
            var hauteur = new float[g.NbCoins];

            for (int coin = 0; coin < g.NbCoins; coin++)
            {
                int deb = g.DebutCellulesDeCoin[coin];
                int fin = g.DebutCellulesDeCoin[coin + 1];
                if (deb == fin) { hauteur[coin] = -p.PenteFondMarin; continue; }

                float sommeDist = 0f, sommeCrete = 0f;
                bool auMoinsUneTerre = false;
                for (int i = deb; i < fin; i++)
                {
                    int cellule = g.CellulesDeCoin[i];
                    sommeDist += carte.DistCote[cellule];
                    sommeCrete += carte.Crete[cellule];
                    if (carte.Terre[cellule]) auMoinsUneTerre = true;
                }
                int n = fin - deb;
                float distance = sommeDist / n;
                float creteMoyenne = sommeCrete / n;

                if (!auMoinsUneTerre)
                {
                    // Franchement en mer : pente régulière vers le large.
                    hauteur[coin] = p.PenteFondMarin * math.min(distance, -1f);
                    continue;
                }

                // Un coin partagé entre terre et mer est sur le rivage, pas sous l'eau.
                if (distance < 0f) distance = 0f;

                float remontee = p.HauteurPlateau * (1f - Tables.ExpNeg(distance / p.EchelleCotiere));
                float attenuation = math.min(1f, distance / 3f);   // pas de bruit sur la plage
                float bruit = p.AmplitudeBruit * Bruit.Fbm(g.Coins[coin], p.OctavesBruit, p.FrequenceBruit, graine);

                float h = p.HauteurPlage + remontee + bruit * attenuation + p.AmplitudeCrete * creteMoyenne;
                hauteur[coin] = math.max(h, p.HauteurPlage);
            }

            return hauteur;
        }

        static void Mesurer(GrapheCellules g, Carte carte, DiagnosticRelief diag)
        {
            float[] h = carte.HauteurCoin;
            diag.HauteurMin = float.MaxValue;
            diag.HauteurMax = float.MinValue;
            for (int i = 0; i < h.Length; i++)
            {
                if (h[i] < diag.HauteurMin) diag.HauteurMin = h[i];
                if (h[i] > diag.HauteurMax) diag.HauteurMax = h[i];
            }

            // Médiane des altitudes sur terre, hors mer.
            var terreCoins = new System.Collections.Generic.List<float>(h.Length / 2);
            for (int coin = 0; coin < g.NbCoins; coin++)
            {
                bool terre = false;
                for (int i = g.DebutCellulesDeCoin[coin]; i < g.DebutCellulesDeCoin[coin + 1]; i++)
                {
                    if (carte.Terre[g.CellulesDeCoin[i]]) { terre = true; break; }
                }
                if (terre) terreCoins.Add(h[coin]);
            }
            if (terreCoins.Count > 0)
            {
                var tri = terreCoins.ToArray();
                Array.Sort(tri);
                diag.HauteurMedianeTerre = tri[tri.Length / 2];
            }

            // Pente le long des arêtes intérieures aux terres.
            double somme = 0.0;
            int compte = 0;
            float maxPente = 0f;
            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;

                float longueur = g.LongueurArete[e];
                if (longueur < 0.5f) continue;
                float denivele = math.abs(h[g.AreteCoinA[e]] - h[g.AreteCoinB[e]]);
                float degres = math.degrees(math.atan(denivele / longueur));
                somme += degres;
                compte++;
                if (degres > maxPente) maxPente = degres;
            }
            diag.PenteMoyenneDegres = compte > 0 ? (float)(somme / compte) : 0f;
            diag.PenteMaxDegres = maxPente;
        }

        static float Quantile(float[] trie, float q)
        {
            if (trie.Length == 0) return 0f;
            int i = (int)(q * (trie.Length - 1) + 0.5f);
            if (i < 0) i = 0;
            if (i >= trie.Length) i = trie.Length - 1;
            return trie[i];
        }

        static float Smoothstep(float bas, float haut, float x)
        {
            float t = (x - bas) / (haut - bas);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }
    }
}
