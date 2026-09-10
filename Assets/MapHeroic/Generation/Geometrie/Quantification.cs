using System;
using Unity.Mathematics;

namespace MapHeroic.Generation.Geometrie
{
    /// <summary>
    /// Toutes les coordonnées du maillage sont des multiples entiers de 1/1024 m (≈ 1 mm).
    ///
    /// Deux raisons. D'abord la comparaison : deux sommets calculés par des chemins différents
    /// tombent sur la même valeur au lieu de différer d'un dernier bit, ce qui permet de les
    /// fusionner par égalité exacte plutôt que par un seuil arbitraire. Ensuite la
    /// sérialisation : une coordonnée quantifiée tient sur 21 bits signés, ce qui autorisera
    /// plus tard des prédicats géométriques en entiers si le déterminisme entre plateformes
    /// devient nécessaire.
    /// </summary>
    public static class Quantification
    {
        /// <summary>Nombre de pas par mètre.</summary>
        public const double Pas = 1024.0;

        /// <summary>Arrondi au pas le plus proche, en entier.</summary>
        public static int VersEntier(double v)
        {
            // Math.Floor(v + 0.5) plutôt que Math.Round : arrondi au plus proche sans
            // convention « au pair », donc indépendant de l'implémentation.
            return (int)Math.Floor(v * Pas + 0.5);
        }

        /// <summary>Valeur arrondie au pas le plus proche.</summary>
        public static float Quantifier(double v)
        {
            return (float)(VersEntier(v) / Pas);
        }

        public static float2 Quantifier(double x, double y)
        {
            return new float2(Quantifier(x), Quantifier(y));
        }

        /// <summary>
        /// Clé de comparaison d'un point quantifié, pour trier ou fusionner des sommets
        /// identiques. Les coordonnées du générateur tiennent largement dans un entier 32 bits.
        /// </summary>
        public static ulong Cle(double x, double y)
        {
            uint qx = unchecked((uint)VersEntier(x));
            uint qy = unchecked((uint)VersEntier(y));
            return ((ulong)qx << 32) | qy;
        }
    }
}
