using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Geometrie
{
    /// <summary>
    /// Échantillonnage à disque de Poisson (algorithme de Bridson, 2007).
    ///
    /// Produit des points séparés d'au moins <c>rayon</c> mais placés au hasard : ni la
    /// régularité d'une grille — qui se verrait immédiatement sur la carte — ni les paquets
    /// et les vides d'un tirage uniforme, qui donneraient des cellules de tailles très
    /// inégales et rendraient impossible la contrainte d'aire à ± 30 %.
    ///
    /// Le coût est linéaire grâce à une grille d'accélération de pas rayon/√2 : chaque case
    /// contient au plus un point, donc tester un candidat revient à examiner ses 24 voisines.
    /// </summary>
    public static class PoissonDisc
    {
        /// <summary>
        /// Points dans [0, taille) × [0, taille), séparés d'au moins <c>rayon</c>.
        /// </summary>
        /// <param name="essais">Candidats testés autour d'un point avant de l'abandonner (30 = valeur de Bridson).</param>
        public static float2[] Echantillonner(float taille, float rayon, int essais, ref Rng rng)
        {
            if (taille <= 0f) throw new ArgumentOutOfRangeException(nameof(taille));
            if (rayon <= 0f) throw new ArgumentOutOfRangeException(nameof(rayon));
            if (essais < 1) essais = 1;

            float pasGrille = rayon / 1.41421356f;          // rayon / √2 : au plus un point par case
            int nbCases = (int)Math.Ceiling(taille / pasGrille);
            var grille = new int[nbCases * nbCases];
            for (int i = 0; i < grille.Length; i++) grille[i] = -1;

            var points = new List<float2>(nbCases * nbCases / 2);
            var actifs = new List<int>(1024);

            // Premier point au centre, légèrement décalé pour que la graine ait un effet.
            float2 depart = new float2(
                taille * 0.5f + (rng.Float01() - 0.5f) * rayon,
                taille * 0.5f + (rng.Float01() - 0.5f) * rayon);
            Ajouter(depart);

            while (actifs.Count > 0)
            {
                int indexActif = rng.Entier(actifs.Count);
                int origine = actifs[indexActif];
                float2 centre = points[origine];
                bool trouve = false;

                for (int essai = 0; essai < essais; essai++)
                {
                    float angle = rng.Float01() * Tables.DeuxPi;
                    // Rayon tiré dans l'anneau [r, 2r] avec une densité uniforme en surface.
                    float distance = rayon * (float)Math.Sqrt(1.0 + 3.0 * rng.Float01());
                    float2 candidat = new float2(
                        centre.x + Tables.Cos(angle) * distance,
                        centre.y + Tables.Sin(angle) * distance);

                    if (candidat.x < 0f || candidat.y < 0f || candidat.x >= taille || candidat.y >= taille) continue;
                    if (!EstLibre(candidat)) continue;

                    Ajouter(candidat);
                    trouve = true;
                    break;
                }

                if (!trouve)
                {
                    // Retrait en O(1) : on remplace par le dernier. L'ordre de la liste des
                    // actifs n'a pas de sens géométrique, seule sa reproductibilité compte.
                    actifs[indexActif] = actifs[actifs.Count - 1];
                    actifs.RemoveAt(actifs.Count - 1);
                }
            }

            return points.ToArray();

            void Ajouter(float2 p)
            {
                int index = points.Count;
                points.Add(p);
                actifs.Add(index);
                int cx = (int)(p.x / pasGrille);
                int cy = (int)(p.y / pasGrille);
                if (cx < 0) cx = 0; else if (cx >= nbCases) cx = nbCases - 1;
                if (cy < 0) cy = 0; else if (cy >= nbCases) cy = nbCases - 1;
                grille[cy * nbCases + cx] = index;
            }

            bool EstLibre(float2 p)
            {
                int cx = (int)(p.x / pasGrille);
                int cy = (int)(p.y / pasGrille);
                int x0 = Math.Max(cx - 2, 0), x1 = Math.Min(cx + 2, nbCases - 1);
                int y0 = Math.Max(cy - 2, 0), y1 = Math.Min(cy + 2, nbCases - 1);
                float rayonCarre = rayon * rayon;

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int autre = grille[y * nbCases + x];
                        if (autre < 0) continue;
                        float2 q = points[autre];
                        float dx = q.x - p.x;
                        float dy = q.y - p.y;
                        if (dx * dx + dy * dy < rayonCarre) return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Anneau de points régulièrement espacés à <c>marge</c> mètres hors du domaine,
        /// coins compris.
        ///
        /// Sans lui, les points du bord se retrouveraient sur l'enveloppe convexe et leurs
        /// cellules de Voronoï seraient infinies — il faudrait alors les découper à la main,
        /// avec tous les cas particuliers que cela suppose. Entourés par cet anneau, tous les
        /// points du domaine sont strictement intérieurs, donc leurs cellules sont fermées par
        /// construction. Les cellules de l'anneau, elles, sont jetées.
        /// </summary>
        public static float2[] Anneau(float taille, float marge, float pas)
        {
            float min = -marge;
            float max = taille + marge;
            int parCote = Math.Max(2, (int)Math.Ceiling((max - min) / pas));
            var points = new List<float2>(parCote * 4);

            for (int i = 0; i < parCote; i++)
            {
                float t = min + (max - min) * i / parCote;
                points.Add(new float2(t, min));     // bas
                points.Add(new float2(max, t));     // droite
                points.Add(new float2(max - (t - min), max));   // haut
                points.Add(new float2(min, max - (t - min)));    // gauche
            }
            return points.ToArray();
        }
    }
}
