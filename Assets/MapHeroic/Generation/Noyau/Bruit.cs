using System;
using Unity.Mathematics;

namespace MapHeroic.Generation.Noyau
{
    /// <summary>
    /// Bruit de valeur 2D à hachage entier, et fBm associé.
    ///
    /// Le treillis n'est pas stocké : la valeur de chaque nœud est le hachage de ses
    /// coordonnées entières et de la graine. Aucun tableau de permutation, donc aucune
    /// dépendance à un ordre d'initialisation, et un champ infini reproductible à l'identique.
    ///
    /// N'utilisez PAS Mathf.PerlinNoise : son implémentation n'est garantie identique ni
    /// entre versions d'Unity ni entre plateformes.
    ///
    /// Interpolation quintique (6t⁵ − 15t⁴ + 10t³) : dérivées première et seconde nulles aux
    /// nœuds, donc pas d'artefact visible en grille sur le relief.
    /// </summary>
    public static class Bruit
    {
        /// <summary>
        /// Les coordonnées de treillis sont bornées à ±2^22 : au-delà, la conversion
        /// float → int n'est plus définie de la même façon sur toutes les architectures.
        /// Le générateur travaille sur 1400 m, on est très loin de cette limite.
        /// </summary>
        const float BorneTreillis = 4194304f;

        /// <summary>Hachage entier d'un nœud du treillis (finaliseur de type xxHash32).</summary>
        public static uint Hash(int x, int y, uint graine)
        {
            unchecked
            {
                uint h = graine + 374761393u;
                h += (uint)x * 3266489917u;
                h = RotationGauche(h, 17) * 668265263u;
                h += (uint)y * 3266489917u;
                h = RotationGauche(h, 17) * 668265263u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Bruit de valeur dans [-1, 1).</summary>
        public static float Valeur(float2 p, uint graine)
        {
            float px = Borner(p.x);
            float py = Borner(p.y);

            float basX = (float)Math.Floor(px);
            float basY = (float)Math.Floor(py);
            int ix = (int)basX;
            int iy = (int)basY;

            float ux = Quintique(px - basX);
            float uy = Quintique(py - basY);

            float v00 = Normaliser(Hash(ix, iy, graine));
            float v10 = Normaliser(Hash(ix + 1, iy, graine));
            float v01 = Normaliser(Hash(ix, iy + 1, graine));
            float v11 = Normaliser(Hash(ix + 1, iy + 1, graine));

            float bas = v00 + (v10 - v00) * ux;
            float haut = v01 + (v11 - v01) * ux;
            return bas + (haut - bas) * uy;
        }

        /// <summary>
        /// Bruit fractionnaire dans [-1, 1] : somme d'octaves de fréquence doublée
        /// (lacunarité 2) et d'amplitude divisée par deux (persistance 0,5), normalisée.
        /// </summary>
        public static float Fbm(float2 p, int octaves, float frequence, uint graine)
        {
            if (octaves < 1) octaves = 1;
            if (octaves > 12) octaves = 12;

            float somme = 0f;
            float amplitude = 1f;
            float normalisation = 0f;
            float f = frequence;

            for (int o = 0; o < octaves; o++)
            {
                unchecked
                {
                    somme += amplitude * Valeur(p * f, graine + (uint)o * 0x9E3779B9u);
                }
                normalisation += amplitude;
                amplitude *= 0.5f;
                f *= 2f;
            }

            return normalisation > 0f ? somme / normalisation : 0f;
        }

        /// <summary>
        /// Bruit « en crêtes » dans [0, 1] : 1 − |fBm|. Les maxima forment des lignes
        /// continues plutôt que des taches — c'est ce qui donne des chaînes de montagnes
        /// plutôt que des massifs isolés.
        /// </summary>
        public static float Crete(float2 p, int octaves, float frequence, uint graine)
        {
            float v = Fbm(p, octaves, frequence, graine);
            return 1f - (v < 0f ? -v : v);
        }

        static float Quintique(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        static float Normaliser(uint h)
        {
            // 24 bits de poids fort → [-1, 1), conversion exacte en float.
            return (h >> 8) * (2f / 16777216f) - 1f;
        }

        static float Borner(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v > BorneTreillis) return BorneTreillis;
            if (v < -BorneTreillis) return -BorneTreillis;
            return v;
        }

        static uint RotationGauche(uint x, int k)
        {
            unchecked
            {
                return (x << k) | (x >> (32 - k));
            }
        }
    }
}
