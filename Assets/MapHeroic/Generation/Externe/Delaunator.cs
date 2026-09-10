// -----------------------------------------------------------------------------------------
// Triangulation de Delaunay — adapté de DelaunatorSharp
// https://github.com/nol1fe/delaunator-sharp
//
// MIT License — Copyright (c) 2019 Patryk Grech
// (lui-même porté de Delaunator, MIT — Copyright (c) 2017 Mapbox)
// Texte complet de la licence dans CREDITS.md à la racine du dépôt.
//
// Adaptations pour Map Heroic :
//  - entrée = tableau plat de doubles (x, y, x, y, …) au lieu d'un tableau d'IPoint : plus
//    d'allocation par point, plus d'indirection d'interface ;
//  - suppression de toute l'API LINQ / IEnumerable (GetTriangles, GetVoronoiCells, …), qui
//    alloue à chaque parcours ; le générateur travaille directement sur Triangles/Halfedges ;
//  - hullHash initialisé à -1 comme dans l'implémentation JavaScript de référence — le port
//    C# l'oubliait, ce qui laissait le point 0 servir de départ de recherche par erreur ;
//  - EPSILON en constante littérale plutôt que Math.Pow(2, -52) : Math.Pow vient de la
//    bibliothèque du système, dont le résultat peut varier d'une plateforme à l'autre.
//
// L'algorithme lui-même (balayage par enveloppe convexe, légalisation) est inchangé.
// -----------------------------------------------------------------------------------------

using System;

namespace DelaunatorSharp
{
    /// <summary>Triangulation de Delaunay en O(n log n) sur un nuage de points 2D.</summary>
    public sealed class Delaunator
    {
        const double Epsilon = 2.220446049250313E-16; // 2^-52

        readonly int[] _pileAretes = new int[512];

        int[] _triangles;
        int[] _halfedges;

        /// <summary>Trois index de point par triangle (une entrée par demi-arête).</summary>
        public int[] Triangles => _triangles;

        /// <summary>Demi-arête opposée dans le triangle adjacent, ou -1 s'il n'y en a pas.</summary>
        public int[] Halfedges => _halfedges;

        /// <summary>Index des points de l'enveloppe convexe, dans l'ordre.</summary>
        public int[] Hull { get; private set; }

        /// <summary>Coordonnées d'entrée, en (x, y) consécutifs.</summary>
        public double[] Coords { get; }

        /// <summary>Nombre de points.</summary>
        public int NbPoints => Coords.Length / 2;

        readonly int _tailleHash;
        readonly int[] _hullPrev;
        readonly int[] _hullNext;
        readonly int[] _hullTri;
        readonly int[] _hullHash;

        double _cx;
        double _cy;
        int _nbTriangles;
        int _hullStart;
        int _hullSize;

        public Delaunator(double[] coords)
        {
            if (coords == null) throw new ArgumentNullException(nameof(coords));
            if (coords.Length % 2 != 0) throw new ArgumentException("Le tableau doit contenir des paires (x, y).", nameof(coords));

            Coords = coords;
            int n = coords.Length / 2;
            if (n < 3) throw new ArgumentOutOfRangeException(nameof(coords), "Il faut au moins 3 points.");

            int maxTriangles = 2 * n - 5;
            _triangles = new int[maxTriangles * 3];
            _halfedges = new int[maxTriangles * 3];
            _tailleHash = (int)Math.Ceiling(Math.Sqrt(n));

            _hullPrev = new int[n];
            _hullNext = new int[n];
            _hullTri = new int[n];
            _hullHash = new int[_tailleHash];
            for (int i = 0; i < _tailleHash; i++) _hullHash[i] = -1;

            var ids = new int[n];

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                double x = coords[2 * i];
                double y = coords[2 * i + 1];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                ids[i] = i;
            }

            double centreX = (minX + maxX) / 2;
            double centreY = (minY + maxY) / 2;

            // Point de départ le plus proche du centre.
            double distMin = double.PositiveInfinity;
            int i0 = 0, i1 = 0, i2 = 0;
            for (int i = 0; i < n; i++)
            {
                double d = Dist(centreX, centreY, coords[2 * i], coords[2 * i + 1]);
                if (d < distMin) { i0 = i; distMin = d; }
            }
            double i0x = coords[2 * i0], i0y = coords[2 * i0 + 1];

            // Point le plus proche du précédent.
            distMin = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (i == i0) continue;
                double d = Dist(i0x, i0y, coords[2 * i], coords[2 * i + 1]);
                if (d < distMin && d > 0) { i1 = i; distMin = d; }
            }
            double i1x = coords[2 * i1], i1y = coords[2 * i1 + 1];

            // Troisième point formant le plus petit cercle circonscrit.
            double rayonMin = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1) continue;
                double r = Circumradius(i0x, i0y, i1x, i1y, coords[2 * i], coords[2 * i + 1]);
                if (r < rayonMin) { i2 = i; rayonMin = r; }
            }
            double i2x = coords[2 * i2], i2y = coords[2 * i2 + 1];

            if (double.IsPositiveInfinity(rayonMin))
            {
                throw new InvalidOperationException("Points tous alignés : aucune triangulation de Delaunay n'existe.");
            }

            if (Orient(i0x, i0y, i1x, i1y, i2x, i2y))
            {
                int it = i1; double tx = i1x, ty = i1y;
                i1 = i2; i1x = i2x; i1y = i2y;
                i2 = it; i2x = tx; i2y = ty;
            }

            Circumcenter(i0x, i0y, i1x, i1y, i2x, i2y, out _cx, out _cy);

            var distances = new double[n];
            for (int i = 0; i < n; i++)
            {
                distances[i] = Dist(coords[2 * i], coords[2 * i + 1], _cx, _cy);
            }
            TriRapide(ids, distances, 0, n - 1);

            _hullStart = i0;
            _hullSize = 3;

            _hullNext[i0] = _hullPrev[i2] = i1;
            _hullNext[i1] = _hullPrev[i0] = i2;
            _hullNext[i2] = _hullPrev[i1] = i0;

            _hullTri[i0] = 0;
            _hullTri[i1] = 1;
            _hullTri[i2] = 2;

            _hullHash[CleHash(i0x, i0y)] = i0;
            _hullHash[CleHash(i1x, i1y)] = i1;
            _hullHash[CleHash(i2x, i2y)] = i2;

            _nbTriangles = 0;
            AjouterTriangle(i0, i1, i2, -1, -1, -1);

            double xp = 0, yp = 0;
            for (int k = 0; k < ids.Length; k++)
            {
                int i = ids[k];
                double x = coords[2 * i];
                double y = coords[2 * i + 1];

                if (k > 0 && Math.Abs(x - xp) <= Epsilon && Math.Abs(y - yp) <= Epsilon) continue;
                xp = x;
                yp = y;

                if (i == i0 || i == i1 || i == i2) continue;

                int debut = 0;
                for (int j = 0; j < _tailleHash; j++)
                {
                    int cle = CleHash(x, y);
                    debut = _hullHash[(cle + j) % _tailleHash];
                    if (debut != -1 && debut != _hullNext[debut]) break;
                }
                if (debut == -1) continue;

                debut = _hullPrev[debut];
                int e = debut;
                int q = _hullNext[e];

                while (!Orient(x, y, coords[2 * e], coords[2 * e + 1], coords[2 * q], coords[2 * q + 1]))
                {
                    e = q;
                    if (e == debut) { e = int.MaxValue; break; }
                    q = _hullNext[e];
                }
                if (e == int.MaxValue) continue;

                int t = AjouterTriangle(e, i, _hullNext[e], -1, -1, _hullTri[e]);
                _hullTri[i] = Legaliser(t + 2);
                _hullTri[e] = t;
                _hullSize++;

                int suivant = _hullNext[e];
                q = _hullNext[suivant];
                while (Orient(x, y, coords[2 * suivant], coords[2 * suivant + 1], coords[2 * q], coords[2 * q + 1]))
                {
                    t = AjouterTriangle(suivant, i, q, _hullTri[i], -1, _hullTri[suivant]);
                    _hullTri[i] = Legaliser(t + 2);
                    _hullNext[suivant] = suivant;
                    _hullSize--;
                    suivant = q;
                    q = _hullNext[suivant];
                }

                if (e == debut)
                {
                    q = _hullPrev[e];
                    while (Orient(x, y, coords[2 * q], coords[2 * q + 1], coords[2 * e], coords[2 * e + 1]))
                    {
                        t = AjouterTriangle(q, i, e, -1, _hullTri[e], _hullTri[q]);
                        Legaliser(t + 2);
                        _hullTri[q] = t;
                        _hullNext[e] = e;
                        _hullSize--;
                        e = q;
                        q = _hullPrev[e];
                    }
                }

                _hullStart = _hullPrev[i] = e;
                _hullNext[e] = _hullPrev[suivant] = i;
                _hullNext[i] = suivant;

                _hullHash[CleHash(x, y)] = i;
                _hullHash[CleHash(coords[2 * e], coords[2 * e + 1])] = e;
            }

            Hull = new int[_hullSize];
            int s = _hullStart;
            for (int i = 0; i < _hullSize; i++)
            {
                Hull[i] = s;
                s = _hullNext[s];
            }

            Array.Resize(ref _triangles, _nbTriangles);
            Array.Resize(ref _halfedges, _nbTriangles);
        }

        int Legaliser(int a)
        {
            int i = 0;
            int ar;

            while (true)
            {
                int b = Halfedges[a];
                int a0 = a - a % 3;
                ar = a0 + (a + 2) % 3;

                if (b == -1)
                {
                    if (i == 0) break;
                    a = _pileAretes[--i];
                    continue;
                }

                int b0 = b - b % 3;
                int al = a0 + (a + 1) % 3;
                int bl = b0 + (b + 2) % 3;

                int p0 = Triangles[ar];
                int pr = Triangles[a];
                int pl = Triangles[al];
                int p1 = Triangles[bl];

                bool illegal = InCircle(
                    Coords[2 * p0], Coords[2 * p0 + 1],
                    Coords[2 * pr], Coords[2 * pr + 1],
                    Coords[2 * pl], Coords[2 * pl + 1],
                    Coords[2 * p1], Coords[2 * p1 + 1]);

                if (illegal)
                {
                    Triangles[a] = p1;
                    Triangles[b] = p0;

                    int hbl = Halfedges[bl];
                    if (hbl == -1)
                    {
                        int e = _hullStart;
                        do
                        {
                            if (_hullTri[e] == bl) { _hullTri[e] = a; break; }
                            e = _hullPrev[e];
                        }
                        while (e != _hullStart);
                    }
                    Lier(a, hbl);
                    Lier(b, Halfedges[ar]);
                    Lier(ar, bl);

                    int br = b0 + (b + 1) % 3;
                    if (i < _pileAretes.Length) _pileAretes[i++] = br;
                }
                else
                {
                    if (i == 0) break;
                    a = _pileAretes[--i];
                }
            }
            return ar;
        }

        int AjouterTriangle(int i0, int i1, int i2, int a, int b, int c)
        {
            int t = _nbTriangles;
            Triangles[t] = i0;
            Triangles[t + 1] = i1;
            Triangles[t + 2] = i2;
            Lier(t, a);
            Lier(t + 1, b);
            Lier(t + 2, c);
            _nbTriangles += 3;
            return t;
        }

        void Lier(int a, int b)
        {
            Halfedges[a] = b;
            if (b != -1) Halfedges[b] = a;
        }

        int CleHash(double x, double y)
        {
            int cle = (int)(Math.Floor(PseudoAngle(x - _cx, y - _cy) * _tailleHash) % _tailleHash);
            return cle < 0 ? cle + _tailleHash : cle;
        }

        static double PseudoAngle(double dx, double dy)
        {
            double p = dx / (Math.Abs(dx) + Math.Abs(dy));
            return (dy > 0 ? 3 - p : 1 + p) / 4;
        }

        static void TriRapide(int[] ids, double[] distances, int gauche, int droite)
        {
            if (droite - gauche <= 20)
            {
                for (int i = gauche + 1; i <= droite; i++)
                {
                    int temp = ids[i];
                    double tempDist = distances[temp];
                    int j = i - 1;
                    while (j >= gauche && distances[ids[j]] > tempDist) ids[j + 1] = ids[j--];
                    ids[j + 1] = temp;
                }
            }
            else
            {
                int median = (gauche + droite) >> 1;
                int i = gauche + 1;
                int j = droite;
                Echanger(ids, median, i);
                if (distances[ids[gauche]] > distances[ids[droite]]) Echanger(ids, gauche, droite);
                if (distances[ids[i]] > distances[ids[droite]]) Echanger(ids, i, droite);
                if (distances[ids[gauche]] > distances[ids[i]]) Echanger(ids, gauche, i);

                int pivot = ids[i];
                double pivotDist = distances[pivot];
                while (true)
                {
                    do i++; while (distances[ids[i]] < pivotDist);
                    do j--; while (distances[ids[j]] > pivotDist);
                    if (j < i) break;
                    Echanger(ids, i, j);
                }
                ids[gauche + 1] = ids[j];
                ids[j] = pivot;

                if (droite - i + 1 >= j - gauche)
                {
                    TriRapide(ids, distances, i, droite);
                    TriRapide(ids, distances, gauche, j - 1);
                }
                else
                {
                    TriRapide(ids, distances, gauche, j - 1);
                    TriRapide(ids, distances, i, droite);
                }
            }
        }

        static void Echanger(int[] t, int i, int j)
        {
            int tmp = t[i];
            t[i] = t[j];
            t[j] = tmp;
        }

        static bool InCircle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            double dx = ax - px, dy = ay - py;
            double ex = bx - px, ey = by - py;
            double fx = cx - px, fy = cy - py;
            double ap = dx * dx + dy * dy;
            double bp = ex * ex + ey * ey;
            double cp = fx * fx + fy * fy;
            return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) < 0;
        }

        static bool Orient(double px, double py, double qx, double qy, double rx, double ry)
        {
            return (qy - py) * (rx - qx) - (qx - px) * (ry - qy) < 0;
        }

        static double Circumradius(double ax, double ay, double bx, double by, double cx, double cy)
        {
            double dx = bx - ax, dy = by - ay;
            double ex = cx - ax, ey = cy - ay;
            double bl = dx * dx + dy * dy;
            double cl = ex * ex + ey * ey;
            double d = 0.5 / (dx * ey - dy * ex);
            double x = (ey * bl - dy * cl) * d;
            double y = (dx * cl - ex * bl) * d;
            return x * x + y * y;
        }

        /// <summary>Centre du cercle circonscrit — c'est un sommet du diagramme de Voronoï.</summary>
        public static void Circumcenter(double ax, double ay, double bx, double by, double cx, double cy,
                                        out double x, out double y)
        {
            double dx = bx - ax, dy = by - ay;
            double ex = cx - ax, ey = cy - ay;
            double bl = dx * dx + dy * dy;
            double cl = ex * ex + ey * ey;
            double d = 0.5 / (dx * ey - dy * ex);
            x = ax + (ey * bl - dy * cl) * d;
            y = ay + (dx * cl - ex * bl) * d;
        }

        static double Dist(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }

        /// <summary>Demi-arête suivante dans le même triangle.</summary>
        public static int DemiAreteSuivante(int e) => (e % 3 == 2) ? e - 2 : e + 1;

        /// <summary>Triangle auquel appartient une demi-arête.</summary>
        public static int TriangleDeArete(int e) => e / 3;
    }
}
