using System;
using System.Collections.Generic;
using System.Diagnostics;
using DelaunatorSharp;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Geometrie
{
    /// <summary>Réglages de la phase géométrique (P1 et P2).</summary>
    public sealed class ParametresMaillage
    {
        /// <summary>Côté du domaine carré, en mètres.</summary>
        public float TailleCarte = 1400f;

        /// <summary>Distance minimale entre deux sites. Fixe la finesse du maillage.</summary>
        public float RayonPoisson = 14f;

        /// <summary>
        /// Candidats testés autour d'un point avant de l'abandonner. Bridson recommande 30 ;
        /// en dessous, l'échantillonnage laisse des interstices et la densité baisse.
        /// </summary>
        public int EssaisPoisson = 30;

        /// <summary>
        /// Passes de relaxation de Lloyd. Zéro donne des cellules très irrégulières, quatre
        /// les rend presque hexagonales — donc visiblement artificielles. Deux est le compromis.
        /// </summary>
        public int IterationsLloyd = 2;

        /// <summary>Distance à laquelle l'anneau de fermeture est posé hors du domaine.</summary>
        public float MargeAnneau = 20f;

        /// <summary>Espacement des points de l'anneau.</summary>
        public float PasAnneau = 20f;
    }

    /// <summary>Mesures relevées pendant la construction, affichées plus tard dans l'éditeur.</summary>
    public sealed class DiagnosticMaillage
    {
        public int NbPointsInterieurs;
        public int NbPointsAnneau;
        public int NbTriangles;
        public int NbSommetsAvantFusion;
        public int NbSommetsApresFusion;

        /// <summary>Cellules écartées faute d'être fermées ou d'avoir trois sommets distincts.</summary>
        public int NbCellulesRejetees;

        public long MillisecondesPoisson;
        public long MillisecondesTriangulations;
        public long MillisecondesAssemblage;
        public long MillisecondesTotal;

        public override string ToString()
        {
            return $"{NbPointsInterieurs} cellules ({NbPointsAnneau} points d'anneau), " +
                   $"{NbTriangles} triangles, {NbSommetsApresFusion} sommets " +
                   $"(fusion : {NbSommetsAvantFusion - NbSommetsApresFusion}), " +
                   $"{NbCellulesRejetees} rejetées, {MillisecondesTotal} ms " +
                   $"(Poisson {MillisecondesPoisson}, triangulations {MillisecondesTriangulations}, " +
                   $"assemblage {MillisecondesAssemblage})";
        }
    }

    /// <summary>
    /// Phases P1 et P2 : nuage de points, triangulation de Delaunay, diagramme de Voronoï,
    /// relaxation de Lloyd, puis assemblage du <see cref="GrapheCellules"/>.
    ///
    /// Les sommets du diagramme de Voronoï sont les centres des cercles circonscrits aux
    /// triangles de Delaunay ; le contour d'une cellule s'obtient en tournant autour de son
    /// site d'un triangle incident au suivant. Comme l'anneau de fermeture met tous les sites
    /// du domaine strictement à l'intérieur de l'enveloppe convexe, ces tours se referment
    /// toujours : aucune cellule infinie à découper.
    /// </summary>
    public static class ConstructeurMaillage
    {
        /// <summary>Identifiant de sous-flux de la phase P1.</summary>
        public const int PhasePoints = 1;

        public static GrapheCellules Construire(ulong graine, ParametresMaillage p, out DiagnosticMaillage diag)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            diag = new DiagnosticMaillage();
            var chronoTotal = Stopwatch.StartNew();
            var chrono = Stopwatch.StartNew();

            // --- P1 : sites ------------------------------------------------------------
            var rngRacine = Rng.DepuisSeed(graine);
            var rngPoints = rngRacine.Deriver(PhasePoints);
            float2[] interieurs = PoissonDisc.Echantillonner(p.TailleCarte, p.RayonPoisson, p.EssaisPoisson, ref rngPoints);
            float2[] anneau = PoissonDisc.Anneau(p.TailleCarte, p.MargeAnneau, p.PasAnneau);

            diag.NbPointsInterieurs = interieurs.Length;
            diag.NbPointsAnneau = anneau.Length;
            diag.MillisecondesPoisson = chrono.ElapsedMilliseconds;

            if (interieurs.Length < 16)
            {
                throw new InvalidOperationException(
                    $"Seulement {interieurs.Length} sites générés : rayon trop grand pour la taille de carte ?");
            }

            int nbInterieurs = interieurs.Length;
            int nbTotal = nbInterieurs + anneau.Length;
            var coords = new double[nbTotal * 2];
            for (int i = 0; i < nbInterieurs; i++)
            {
                coords[2 * i] = Quantification.Quantifier(interieurs[i].x);
                coords[2 * i + 1] = Quantification.Quantifier(interieurs[i].y);
            }
            for (int i = 0; i < anneau.Length; i++)
            {
                coords[2 * (nbInterieurs + i)] = Quantification.Quantifier(anneau[i].x);
                coords[2 * (nbInterieurs + i) + 1] = Quantification.Quantifier(anneau[i].y);
            }

            // --- P2 : Delaunay, Voronoï, Lloyd ------------------------------------------
            chrono.Restart();
            double borneMin = -2.0 * p.MargeAnneau;
            double borneMax = p.TailleCarte + 2.0 * p.MargeAnneau;

            Delaunator triangulation = null;
            double[] sommets = null;
            int[][] contours = null;

            int nbPasses = Math.Max(0, p.IterationsLloyd) + 1;
            for (int passe = 0; passe < nbPasses; passe++)
            {
                triangulation = new Delaunator(coords);
                sommets = Circumcentres(triangulation, borneMin, borneMax);
                contours = Contours(triangulation, nbInterieurs, sommets);

                if (passe == nbPasses - 1) break;

                // Relaxation : chaque site rejoint le centre de gravité de sa cellule.
                for (int i = 0; i < nbInterieurs; i++)
                {
                    int[] contour = contours[i];
                    if (contour == null) continue;
                    Centroide(contour, sommets, out double cx, out double cy);
                    if (cx < 0.0) cx = 0.0; else if (cx > p.TailleCarte) cx = p.TailleCarte;
                    if (cy < 0.0) cy = 0.0; else if (cy > p.TailleCarte) cy = p.TailleCarte;
                    coords[2 * i] = Quantification.Quantifier(cx);
                    coords[2 * i + 1] = Quantification.Quantifier(cy);
                }
            }

            diag.NbTriangles = triangulation.Triangles.Length / 3;
            diag.MillisecondesTriangulations = chrono.ElapsedMilliseconds;

            // --- Assemblage du graphe ----------------------------------------------------
            chrono.Restart();
            var graphe = Assembler(coords, nbInterieurs, sommets, contours, diag);
            graphe.ReconstruireDerives();
            diag.MillisecondesAssemblage = chrono.ElapsedMilliseconds;
            diag.MillisecondesTotal = chronoTotal.ElapsedMilliseconds;

            return graphe;
        }

        /// <summary>
        /// Un sommet de Voronoï par triangle. Les triangles très aplatis ont un centre
        /// rejeté à l'infini ; on le ramène dans une boîte élargie plutôt que de laisser
        /// filer des coordonnées absurdes — ces triangles bordent l'anneau, dont les cellules
        /// sont jetées de toute façon.
        /// </summary>
        static double[] Circumcentres(Delaunator del, double min, double max)
        {
            int nbTriangles = del.Triangles.Length / 3;
            var sommets = new double[nbTriangles * 2];
            double[] c = del.Coords;

            for (int t = 0; t < nbTriangles; t++)
            {
                int a = del.Triangles[3 * t];
                int b = del.Triangles[3 * t + 1];
                int d = del.Triangles[3 * t + 2];
                Delaunator.Circumcenter(
                    c[2 * a], c[2 * a + 1],
                    c[2 * b], c[2 * b + 1],
                    c[2 * d], c[2 * d + 1],
                    out double x, out double y);

                // Comparaisons inversées pour que NaN soit aussi ramené dans la boîte.
                if (!(x >= min)) x = min; else if (!(x <= max)) x = max;
                if (!(y >= min)) y = min; else if (!(y <= max)) y = max;

                sommets[2 * t] = x;
                sommets[2 * t + 1] = y;
            }
            return sommets;
        }

        /// <summary>
        /// Contour de chaque cellule intérieure, en index de triangles, sens trigonométrique.
        /// Renvoie null pour une cellule qui ne se referme pas (le site est alors sur
        /// l'enveloppe convexe, ce qui ne devrait pas arriver avec l'anneau de fermeture).
        /// </summary>
        static int[][] Contours(Delaunator del, int nbInterieurs, double[] sommets)
        {
            int[] triangles = del.Triangles;
            int[] demiAretes = del.Halfedges;

            // Une demi-arête arrivant sur chaque point sert de départ au tour.
            var entrante = new int[del.NbPoints];
            for (int i = 0; i < entrante.Length; i++) entrante[i] = -1;
            for (int e = 0; e < triangles.Length; e++)
            {
                int arrivee = triangles[Delaunator.DemiAreteSuivante(e)];
                if (entrante[arrivee] == -1 || demiAretes[e] == -1) entrante[arrivee] = e;
            }

            var resultat = new int[nbInterieurs][];
            var tampon = new List<int>(16);

            for (int point = 0; point < nbInterieurs; point++)
            {
                int depart = entrante[point];
                if (depart == -1) { resultat[point] = null; continue; }

                tampon.Clear();
                int e = depart;
                bool ferme = false;
                for (int garde = 0; garde < 512; garde++)
                {
                    tampon.Add(Delaunator.TriangleDeArete(e));
                    e = demiAretes[Delaunator.DemiAreteSuivante(e)];
                    if (e == -1) break;
                    if (e == depart) { ferme = true; break; }
                }

                if (!ferme || tampon.Count < 3) { resultat[point] = null; continue; }

                var contour = tampon.ToArray();
                if (AireSignee(contour, sommets) < 0.0) Array.Reverse(contour);
                resultat[point] = contour;
            }
            return resultat;
        }

        static double AireSignee(int[] contour, double[] sommets)
        {
            double somme = 0.0;
            int n = contour.Length;
            for (int k = 0; k < n; k++)
            {
                int i = contour[k];
                int j = contour[k + 1 == n ? 0 : k + 1];
                somme += sommets[2 * i] * sommets[2 * j + 1] - sommets[2 * j] * sommets[2 * i + 1];
            }
            return somme * 0.5;
        }

        static void Centroide(int[] contour, double[] sommets, out double cx, out double cy)
        {
            int n = contour.Length;
            double aireDouble = 0.0, sx = 0.0, sy = 0.0;

            for (int k = 0; k < n; k++)
            {
                int i = contour[k];
                int j = contour[k + 1 == n ? 0 : k + 1];
                double px = sommets[2 * i], py = sommets[2 * i + 1];
                double qx = sommets[2 * j], qy = sommets[2 * j + 1];
                double croix = px * qy - qx * py;
                aireDouble += croix;
                sx += (px + qx) * croix;
                sy += (py + qy) * croix;
            }

            if (Math.Abs(aireDouble) < 1e-9)
            {
                // Cellule d'aire nulle : on se rabat sur la moyenne des sommets.
                sx = 0.0; sy = 0.0;
                for (int k = 0; k < n; k++)
                {
                    sx += sommets[2 * contour[k]];
                    sy += sommets[2 * contour[k] + 1];
                }
                cx = sx / n;
                cy = sy / n;
                return;
            }

            cx = sx / (3.0 * aireDouble);
            cy = sy / (3.0 * aireDouble);
        }

        /// <summary>
        /// Quantifie et fusionne les sommets, écarte les cellules dégénérées, renumérote, et
        /// remplit la structure CSR.
        ///
        /// La fusion par égalité exacte des coordonnées quantifiées n'est pas un artifice :
        /// quatre sites cocycliques partagent réellement un unique sommet de Voronoï, et deux
        /// triangles voisins produisent alors le même centre. Sans fusion, la cellule
        /// porterait une arête de longueur nulle et l'appariement des arêtes échouerait.
        /// </summary>
        static GrapheCellules Assembler(double[] coords, int nbInterieurs, double[] sommets,
                                        int[][] contours, DiagnosticMaillage diag)
        {
            int nbSommets = sommets.Length / 2;
            diag.NbSommetsAvantFusion = nbSommets;

            var cles = new ulong[nbSommets];
            for (int t = 0; t < nbSommets; t++)
            {
                cles[t] = Quantification.Cle(sommets[2 * t], sommets[2 * t + 1]);
            }

            var ordre = new int[nbSommets];
            for (int i = 0; i < nbSommets; i++) ordre[i] = i;
            Array.Sort(ordre, (a, b) =>
            {
                if (cles[a] != cles[b]) return cles[a] < cles[b] ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            var canonique = new int[nbSommets];
            var fusionnesX = new List<float>(nbSommets);
            var fusionnesY = new List<float>(nbSommets);
            for (int i = 0; i < nbSommets; i++)
            {
                int t = ordre[i];
                if (i == 0 || cles[t] != cles[ordre[i - 1]])
                {
                    fusionnesX.Add(Quantification.Quantifier(sommets[2 * t]));
                    fusionnesY.Add(Quantification.Quantifier(sommets[2 * t + 1]));
                }
                canonique[t] = fusionnesX.Count - 1;
            }
            diag.NbSommetsApresFusion = fusionnesX.Count;

            var listes = new List<int[]>(nbInterieurs);
            var sites = new List<float2>(nbInterieurs);
            var tampon = new List<int>(16);

            for (int point = 0; point < nbInterieurs; point++)
            {
                int[] contour = contours[point];
                if (contour == null) { diag.NbCellulesRejetees++; continue; }

                tampon.Clear();
                for (int k = 0; k < contour.Length; k++)
                {
                    int coin = canonique[contour[k]];
                    if (tampon.Count > 0 && tampon[tampon.Count - 1] == coin) continue;
                    tampon.Add(coin);
                }
                while (tampon.Count > 1 && tampon[0] == tampon[tampon.Count - 1])
                {
                    tampon.RemoveAt(tampon.Count - 1);
                }

                if (tampon.Count < 3) { diag.NbCellulesRejetees++; continue; }

                listes.Add(tampon.ToArray());
                sites.Add(new float2(
                    Quantification.Quantifier(coords[2 * point]),
                    Quantification.Quantifier(coords[2 * point + 1])));
            }

            // Renumérotation des seuls sommets réellement utilisés, dans l'ordre croissant
            // des anciens index — la numérotation reste donc reproductible.
            var nouveauIndex = new int[fusionnesX.Count];
            for (int i = 0; i < nouveauIndex.Length; i++) nouveauIndex[i] = -1;
            foreach (int[] liste in listes)
            {
                for (int k = 0; k < liste.Length; k++) nouveauIndex[liste[k]] = 0;
            }
            int nbCoinsUtiles = 0;
            for (int i = 0; i < nouveauIndex.Length; i++)
            {
                if (nouveauIndex[i] == 0) nouveauIndex[i] = nbCoinsUtiles++;
            }

            var graphe = new GrapheCellules
            {
                NbCellules = listes.Count,
                NbCoins = nbCoinsUtiles,
                Coins = new float2[nbCoinsUtiles],
                Sites = sites.ToArray(),
                DebutCoins = new int[listes.Count + 1]
            };

            for (int i = 0; i < nouveauIndex.Length; i++)
            {
                if (nouveauIndex[i] >= 0) graphe.Coins[nouveauIndex[i]] = new float2(fusionnesX[i], fusionnesY[i]);
            }

            int total = 0;
            for (int c = 0; c < listes.Count; c++)
            {
                graphe.DebutCoins[c] = total;
                total += listes[c].Length;
            }
            graphe.DebutCoins[listes.Count] = total;

            graphe.CoinsDeCellule = new int[total];
            int curseur = 0;
            for (int c = 0; c < listes.Count; c++)
            {
                int[] liste = listes[c];
                for (int k = 0; k < liste.Length; k++) graphe.CoinsDeCellule[curseur++] = nouveauIndex[liste[k]];
            }

            return graphe;
        }
    }
}
