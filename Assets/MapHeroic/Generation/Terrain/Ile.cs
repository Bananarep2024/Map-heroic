using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresIle
    {
        public float TailleCarte = 1400f;

        /// <summary>
        /// Bande côtière forcée en mer. Elle garantit que l'île ne touche jamais le bord du
        /// domaine — sans quoi une côte serait tranchée net, et le maillage y est de toute
        /// façon déformé par l'anneau de fermeture.
        /// </summary>
        public float MargeBord = 48f;

        public float RatioTerreMin = 0.26f;
        public float RatioTerreMax = 0.38f;

        /// <summary>Tentatives avec des sous-flux successifs avant d'abandonner la graine.</summary>
        public int EssaisMax = 8;

        /// <summary>
        /// Une presqu'île reliée au reste par une seule cellule et plus petite que ce seuil
        /// est rendue à la mer. Au-delà, l'isthme est jugé structurant et la carte rejetée :
        /// aucun découpage ultérieur ne pourrait rendre ses zones franchissables autrement
        /// que par ce goulet unique.
        /// </summary>
        public int TailleLanguetteMax = 6;

        /// <summary>Passes de retrait des languettes avant d'abandonner.</summary>
        public int PassesArticulations = 5;
    }

    public sealed class DiagnosticIle
    {
        public int Essais;
        public bool Reussi;
        public string MotifEchec;
        public float RatioTerre;
        public int NbCellulesTerre;
        public int NbIlotsSupprimes;
        public int NbCellulesIlots;
        public int NbPochesComblees;
        public int NbLanguettesSupprimees;
        public int NbCellulesLanguettes;

        public override string ToString()
        {
            return Reussi
                ? $"île OK en {Essais} essai(s) : {NbCellulesTerre} cellules ({RatioTerre:P1}), " +
                  $"{NbIlotsSupprimes} îlots ({NbCellulesIlots} cellules), {NbPochesComblees} poches comblées, " +
                  $"{NbLanguettesSupprimees} languettes ({NbCellulesLanguettes} cellules)"
                : $"île ÉCHEC après {Essais} essai(s) : {MotifEchec}";
        }
    }

    /// <summary>
    /// Phase P3 : découpe terre/mer d'une île unique, puis distance au rivage.
    ///
    /// La forme vient d'un rayon variable en fonction de l'angle — deux sinusoïdes de
    /// périodes différentes pour les grands lobes, du bruit fractionnaire pour les
    /// découpes fines. Un simple disque bruité donnerait une patate ; les sinusoïdes créent
    /// des caps et des baies à l'échelle où le joueur les lira.
    ///
    /// Le nettoyage qui suit compte autant que le tirage : on ne garde qu'une composante
    /// terrestre, on comble les poches de mer refermées, et on retire les presqu'îles
    /// suspendues à une seule cellule. Ces cas ne sont pas des curiosités : ils casseraient
    /// plus tard la connexité des zones ou des groupes, et il est bien moins coûteux de les
    /// éliminer ici que de les rattraper en P5 ou en P6.
    /// </summary>
    public static class Ile
    {
        public const int Phase = 3;

        public static bool Construire(Carte carte, ParametresIle p, Rng rngRacine, out DiagnosticIle diag)
        {
            if (carte?.Graphe == null) throw new ArgumentNullException(nameof(carte));
            if (p == null) throw new ArgumentNullException(nameof(p));

            diag = new DiagnosticIle();
            GrapheCellules g = carte.Graphe;

            for (int essai = 0; essai < p.EssaisMax; essai++)
            {
                diag.Essais = essai + 1;
                var rng = rngRacine.Deriver(Phase * 100 + essai);
                uint graineForme = rng.Suivant();

                bool[] terre = TirerForme(g, p, graineForme);
                var local = new DiagnosticIle { Essais = diag.Essais };

                if (!Nettoyer(g, p, terre, local, out int[][] poches)) { diag = local; continue; }

                float ratio = ComptervVrais(terre) / (float)g.NbCellules;
                local.RatioTerre = ratio;
                local.NbCellulesTerre = ComptervVrais(terre);

                if (ratio < p.RatioTerreMin || ratio > p.RatioTerreMax)
                {
                    local.MotifEchec = $"ratio de terre {ratio:P1} hors de [{p.RatioTerreMin:P0} ; {p.RatioTerreMax:P0}]";
                    diag = local;
                    continue;
                }

                local.Reussi = true;
                diag = local;

                carte.Terre = terre;
                carte.PochesInterieures.Clear();
                carte.PochesInterieures.AddRange(poches);
                carte.DistCote = CalculerDistanceCote(g, terre);
                return true;
            }

            diag.Reussi = false;
            if (string.IsNullOrEmpty(diag.MotifEchec)) diag.MotifEchec = "aucun essai concluant";
            return false;
        }

        // ------------------------------------------------------------------ forme brute

        /// <summary>
        /// Rayon de l'île en fonction de l'angle, perturbé par du bruit fractionnaire.
        /// Rayon moyen 0,62 dans le carré normalisé [-1, 1]², soit ≈ 31 % de la surface.
        /// </summary>
        public static bool EstTerre(float2 site, float taille, uint graine)
        {
            float2 centre = site / (taille * 0.5f) - 1f;
            float rayon = math.length(centre);
            float angle = Tables.Atan2(centre.y, centre.x);

            float radial = 0.62f
                         + 0.12f * Tables.Sin(3f * angle + 1.7f)
                         + 0.06f * Tables.Sin(7f * angle + 4.1f);

            float bruit = Bruit.Fbm(site, 4, 0.003f, graine);
            return rayon < radial + 0.08f * bruit;
        }

        static bool[] TirerForme(GrapheCellules g, ParametresIle p, uint graineForme)
        {
            var terre = new bool[g.NbCellules];
            float limiteHaute = p.TailleCarte - p.MargeBord;

            for (int c = 0; c < g.NbCellules; c++)
            {
                float2 s = g.Sites[c];
                if (s.x < p.MargeBord || s.y < p.MargeBord || s.x > limiteHaute || s.y > limiteHaute) continue;
                if (ToucheLeBordDuMaillage(g, c)) continue;
                terre[c] = EstTerre(s, p.TailleCarte, graineForme);
            }
            return terre;
        }

        static bool ToucheLeBordDuMaillage(GrapheCellules g, int cellule)
        {
            for (int s = g.DebutCoins[cellule]; s < g.DebutCoins[cellule + 1]; s++)
            {
                if (g.VoisinsDeCellule[s] < 0) return true;
            }
            return false;
        }

        // -------------------------------------------------------------------- nettoyage

        static bool Nettoyer(GrapheCellules g, ParametresIle p, bool[] terre, DiagnosticIle diag,
                             out int[][] poches)
        {
            poches = Array.Empty<int[]>();

            if (!GarderPlusGrandeComposante(g, terre, diag))
            {
                diag.MotifEchec = "aucune cellule de terre";
                return false;
            }

            poches = ComblerPochesInterieures(g, terre, diag);

            for (int passe = 0; passe < p.PassesArticulations; passe++)
            {
                int retirees = RetirerLanguettes(g, p, terre, diag, out bool isthmeStructurant);
                if (isthmeStructurant)
                {
                    diag.MotifEchec = "isthme d'une seule cellule séparant deux grandes parties de l'île";
                    return false;
                }
                if (retirees == 0) return true;

                // Retirer des cellules peut détacher un morceau : on reconsolide.
                if (!GarderPlusGrandeComposante(g, terre, diag))
                {
                    diag.MotifEchec = "l'île s'est vidée au nettoyage";
                    return false;
                }
            }

            diag.MotifEchec = "les languettes ne se stabilisent pas";
            return false;
        }

        static bool GarderPlusGrandeComposante(GrapheCellules g, bool[] terre, DiagnosticIle diag)
        {
            var composante = new int[g.NbCellules];
            for (int i = 0; i < composante.Length; i++) composante[i] = -1;

            var file = new int[g.NbCellules];
            int nbComposantes = 0;
            int meilleure = -1, meilleureTaille = 0;
            var tailles = new List<int>();

            for (int depart = 0; depart < g.NbCellules; depart++)
            {
                if (!terre[depart] || composante[depart] != -1) continue;

                int tete = 0, queue = 0;
                file[queue++] = depart;
                composante[depart] = nbComposantes;
                int taille = 0;

                while (tete < queue)
                {
                    int c = file[tete++];
                    taille++;
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || !terre[v] || composante[v] != -1) continue;
                        composante[v] = nbComposantes;
                        file[queue++] = v;
                    }
                }

                tailles.Add(taille);
                if (taille > meilleureTaille) { meilleureTaille = taille; meilleure = nbComposantes; }
                nbComposantes++;
            }

            if (meilleure < 0) return false;

            for (int c = 0; c < g.NbCellules; c++)
            {
                if (terre[c] && composante[c] != meilleure) terre[c] = false;
            }

            for (int i = 0; i < tailles.Count; i++)
            {
                if (i == meilleure) continue;
                diag.NbIlotsSupprimes++;
                diag.NbCellulesIlots += tailles[i];
            }
            return true;
        }

        /// <summary>
        /// Une poche de mer que les terres ont refermée n'est pas la mer : c'est un lac.
        /// On la rend à la terre ici et on la mémorise, P8 décidera d'y mettre de l'eau.
        /// </summary>
        static int[][] ComblerPochesInterieures(GrapheCellules g, bool[] terre, DiagnosticIle diag)
        {
            var atteint = new bool[g.NbCellules];
            var file = new int[g.NbCellules];
            int tete = 0, queue = 0;

            for (int c = 0; c < g.NbCellules; c++)
            {
                if (terre[c] || atteint[c]) continue;
                if (!ToucheLeBordDuMaillage(g, c)) continue;
                atteint[c] = true;
                file[queue++] = c;
            }

            while (tete < queue)
            {
                int c = file[tete++];
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || terre[v] || atteint[v]) continue;
                    atteint[v] = true;
                    file[queue++] = v;
                }
            }

            var poches = new List<int[]>();
            var membres = new List<int>();

            for (int depart = 0; depart < g.NbCellules; depart++)
            {
                if (terre[depart] || atteint[depart]) continue;

                membres.Clear();
                tete = 0; queue = 0;
                file[queue++] = depart;
                atteint[depart] = true;

                while (tete < queue)
                {
                    int c = file[tete++];
                    membres.Add(c);
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || terre[v] || atteint[v]) continue;
                        atteint[v] = true;
                        file[queue++] = v;
                    }
                }

                foreach (int c in membres) terre[c] = true;
                poches.Add(membres.ToArray());
                diag.NbPochesComblees++;
            }

            return poches.ToArray();
        }

        /// <summary>
        /// Retire les presqu'îles suspendues à une unique cellule. Renvoie le nombre de
        /// cellules rendues à la mer, et signale un isthme trop gros pour être supprimé.
        /// </summary>
        static int RetirerLanguettes(GrapheCellules g, ParametresIle p, bool[] terre,
                                     DiagnosticIle diag, out bool isthmeStructurant)
        {
            isthmeStructurant = false;

            var articulations = ArticulationsEtSousArbres(g, terre, out int[] tailleSousArbre, out int[] parentDfs);
            if (articulations.Count == 0) return 0;

            int retirees = 0;
            foreach ((int _, int enfant, int taille) in articulations)
            {
                if (taille <= p.TailleLanguetteMax)
                {
                    retirees += SupprimerSousArbre(g, terre, enfant, parentDfs);
                    diag.NbLanguettesSupprimees++;
                    diag.NbCellulesLanguettes += taille;
                }
                else
                {
                    isthmeStructurant = true;
                    return retirees;
                }
            }
            return retirees;
        }

        static int SupprimerSousArbre(GrapheCellules g, bool[] terre, int racine, int[] parentDfs)
        {
            // Le sous-arbre séparé est la composante contenant `racine` une fois son parent
            // DFS retiré : un simple parcours suffit, sans reconstruire l'arbre.
            int coupure = parentDfs[racine];
            var file = new int[g.NbCellules];
            int tete = 0, queue = 0;
            var vu = new bool[g.NbCellules];

            file[queue++] = racine;
            vu[racine] = true;
            int n = 0;

            while (tete < queue)
            {
                int c = file[tete++];
                n++;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || !terre[v] || vu[v] || v == coupure) continue;
                    vu[v] = true;
                    file[queue++] = v;
                }
            }

            for (int c = 0; c < g.NbCellules; c++)
            {
                if (vu[c]) terre[c] = false;
            }
            return n;
        }

        /// <summary>
        /// Points d'articulation par un parcours en profondeur itératif (Tarjan), avec la
        /// taille du sous-arbre que chacun sépare. Itératif et non récursif : une île étroite
        /// peut donner une profondeur de plusieurs milliers de cellules.
        /// </summary>
        static List<(int articulation, int enfant, int taille)> ArticulationsEtSousArbres(
            GrapheCellules g, bool[] terre, out int[] tailleSousArbre, out int[] parentDfs)
        {
            int n = g.NbCellules;
            var decouverte = new int[n];
            var basse = new int[n];
            var parent = new int[n];
            var taille = new int[n];
            for (int i = 0; i < n; i++) { decouverte[i] = -1; parent[i] = -1; taille[i] = 1; }

            var resultat = new List<(int, int, int)>();
            var pileNoeud = new int[n + 1];
            var pileCreneau = new int[n + 1];
            int temps = 0;

            for (int racine = 0; racine < n; racine++)
            {
                if (!terre[racine] || decouverte[racine] != -1) continue;

                int sommet = 0;
                pileNoeud[0] = racine;
                pileCreneau[0] = g.DebutCoins[racine];
                decouverte[racine] = basse[racine] = temps++;

                // Les enfants de la racine sont mis de côté : on ne saura qu'à la fin du
                // parcours si elle en a plusieurs, et donc si elle est une articulation.
                // Les traiter au fil de l'eau laisserait passer le premier d'entre eux.
                var enfantsRacine = new List<(int, int, int)>();

                while (sommet >= 0)
                {
                    int u = pileNoeud[sommet];
                    if (pileCreneau[sommet] < g.DebutCoins[u + 1])
                    {
                        int s = pileCreneau[sommet]++;
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || !terre[v] || v == parent[u]) continue;

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
                            pileCreneau[sommet] = g.DebutCoins[v];
                        }
                    }
                    else
                    {
                        sommet--;
                        if (sommet < 0) break;
                        int pere = pileNoeud[sommet];
                        taille[pere] += taille[u];
                        if (basse[u] < basse[pere]) basse[pere] = basse[u];

                        if (pere == racine) enfantsRacine.Add((pere, u, taille[u]));
                        else if (basse[u] >= decouverte[pere]) resultat.Add((pere, u, taille[u]));
                    }
                }

                if (enfantsRacine.Count > 1) resultat.AddRange(enfantsRacine);
            }

            tailleSousArbre = taille;
            parentDfs = parent;
            return resultat;
        }

        // ------------------------------------------------------------ distance au rivage

        /// <summary>
        /// Distance au rivage en nombre de cellules, signée : positive sur terre (0 = plage),
        /// négative en mer (-1 = mer bordant la terre).
        /// </summary>
        public static int[] CalculerDistanceCote(GrapheCellules g, bool[] terre)
        {
            var dist = new int[g.NbCellules];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;

            var file = new int[g.NbCellules];
            int tete = 0, queue = 0;

            // Côté terre : les cellules qui touchent la mer sont à 0.
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (!terre[c]) continue;
                if (!ToucheLaMer(g, terre, c)) continue;
                dist[c] = 0;
                file[queue++] = c;
            }
            PropagerBfs(g, terre, dist, file, ref tete, ref queue, true);

            // Côté mer : -1, -2, … depuis les cellules qui touchent la terre.
            for (int i = 0; i < dist.Length; i++) { if (!terre[i]) dist[i] = int.MinValue; }
            tete = 0; queue = 0;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (terre[c]) continue;
                if (!ToucheLaTerre(g, terre, c)) continue;
                dist[c] = -1;
                file[queue++] = c;
            }
            PropagerBfs(g, terre, dist, file, ref tete, ref queue, false);

            // Les cellules isolées de tout rivage (île sans mer voisine, impossible en
            // pratique) reçoivent une valeur bornée plutôt qu'un extrême.
            for (int i = 0; i < dist.Length; i++)
            {
                if (dist[i] == int.MaxValue) dist[i] = 999;
                else if (dist[i] == int.MinValue) dist[i] = -999;
            }
            return dist;
        }

        static void PropagerBfs(GrapheCellules g, bool[] terre, int[] dist, int[] file,
                                ref int tete, ref int queue, bool cotéTerre)
        {
            while (tete < queue)
            {
                int c = file[tete++];
                int suivant = cotéTerre ? dist[c] + 1 : dist[c] - 1;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || terre[v] != cotéTerre) continue;
                    bool aMettreAJour = cotéTerre ? dist[v] == int.MaxValue : dist[v] == int.MinValue;
                    if (!aMettreAJour) continue;
                    dist[v] = suivant;
                    file[queue++] = v;
                }
            }
        }

        static bool ToucheLaMer(GrapheCellules g, bool[] terre, int c)
        {
            for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
            {
                int v = g.VoisinsDeCellule[s];
                if (v < 0 || !terre[v]) return true;
            }
            return false;
        }

        static bool ToucheLaTerre(GrapheCellules g, bool[] terre, int c)
        {
            for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
            {
                int v = g.VoisinsDeCellule[s];
                if (v >= 0 && terre[v]) return true;
            }
            return false;
        }

        static int ComptervVrais(bool[] t)
        {
            int n = 0;
            for (int i = 0; i < t.Length; i++) { if (t[i]) n++; }
            return n;
        }
    }
}
