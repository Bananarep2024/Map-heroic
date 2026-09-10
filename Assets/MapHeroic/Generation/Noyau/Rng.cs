namespace MapHeroic.Generation.Noyau
{
    /// <summary>
    /// Générateur pseudo-aléatoire déterministe (xoshiro128**), sans allocation.
    ///
    /// Contrat : à graine égale, la suite de tirages est identique partout — seules des
    /// opérations entières 32/64 bits interviennent. N'utilisez JAMAIS System.Random ni
    /// UnityEngine.Random dans le pipeline de génération : ni l'un ni l'autre ne garantit
    /// la même suite d'une version ou d'une plateforme à l'autre.
    ///
    /// Les sous-flux se dérivent de la GRAINE D'ORIGINE, jamais de l'état courant :
    /// <c>racine.Deriver(3)</c> rend toujours le même flux, que la racine ait déjà été
    /// consommée ou non. L'ordre d'exécution des phases est donc sans effet sur leurs tirages,
    /// ce qui évite le piège classique « la phase 5 change parce que la phase 3 a tiré un
    /// nombre de plus ».
    /// </summary>
    public struct Rng
    {
        /// <summary>Nombre d'or sur 64 bits : constante de mélange de SplitMix64.</summary>
        internal const ulong Phi = 0x9E3779B97F4A7C15UL;

        ulong _graine;
        uint _e0, _e1, _e2, _e3;

        /// <summary>Graine d'origine, conservée pour <see cref="Deriver"/>.</summary>
        public ulong Graine => _graine;

        /// <summary>Construit un flux à partir d'une graine (l'état nul est impossible).</summary>
        public static Rng DepuisSeed(ulong graine)
        {
            Rng r = default;
            r._graine = graine;
            ulong x = graine;
            do
            {
                ulong a = SplitMix64(ref x);
                ulong b = SplitMix64(ref x);
                r._e0 = (uint)a;
                r._e1 = (uint)(a >> 32);
                r._e2 = (uint)b;
                r._e3 = (uint)(b >> 32);
            }
            // L'état entièrement nul est un point fixe de xoshiro : il ne produirait que des zéros.
            while ((r._e0 | r._e1 | r._e2 | r._e3) == 0u);
            return r;
        }

        /// <summary>
        /// Sous-flux indépendant, reproductible et indépendant de la consommation de ce flux-ci.
        /// Convention du projet : <c>idPhase</c> = numéro de phase (P1 = 1, P2 = 2, …), et
        /// <c>idPhase * 100 + numeroRejeu</c> pour un rejeu de phase.
        /// </summary>
        public Rng Deriver(int idPhase)
        {
            unchecked
            {
                ulong x = _graine ^ (Phi * ((ulong)(uint)idPhase + 1UL));
                return DepuisSeed(SplitMix64(ref x));
            }
        }

        /// <summary>Tirage brut sur 32 bits.</summary>
        public uint Suivant()
        {
            unchecked
            {
                uint resultat = RotationGauche(_e1 * 5u, 7) * 9u;
                uint t = _e1 << 9;
                _e2 ^= _e0;
                _e3 ^= _e1;
                _e1 ^= _e2;
                _e0 ^= _e3;
                _e2 ^= t;
                _e3 = RotationGauche(_e3, 11);
                return resultat;
            }
        }

        /// <summary>Flottant dans [0, 1). 24 bits de mantisse : la conversion est exacte.</summary>
        public float Float01()
        {
            return (Suivant() >> 8) * (1f / 16777216f);
        }

        /// <summary>Flottant dans [min, max).</summary>
        public float Float(float min, float max)
        {
            return min + (max - min) * Float01();
        }

        /// <summary>
        /// Entier dans [min, maxExclu). Le biais du modulo est de l'ordre de
        /// etendue / 2^32 — négligeable pour les étendues du générateur (au plus quelques
        /// milliers) et, surtout, parfaitement déterministe.
        /// </summary>
        public int Entier(int min, int maxExclu)
        {
            uint etendue = (uint)(maxExclu - min);
            if (etendue == 0u) return min;
            return min + (int)(Suivant() % etendue);
        }

        /// <summary>Entier dans [0, maxExclu).</summary>
        public int Entier(int maxExclu)
        {
            return Entier(0, maxExclu);
        }

        /// <summary>Vrai avec la probabilité indiquée.</summary>
        public bool Chance(float probabilite)
        {
            return Float01() < probabilite;
        }

        /// <summary>Mélange sur place (Fisher-Yates), sans allocation.</summary>
        public void Melanger<T>(T[] tableau)
        {
            if (tableau == null) return;
            for (int i = tableau.Length - 1; i > 0; i--)
            {
                int j = Entier(0, i + 1);
                T tmp = tableau[i];
                tableau[i] = tableau[j];
                tableau[j] = tmp;
            }
        }

        /// <summary>
        /// Index tiré selon des poids positifs. Renvoie -1 si tous les poids sont nuls.
        /// </summary>
        public int IndexPondere(float[] poids)
        {
            if (poids == null || poids.Length == 0) return -1;
            float total = 0f;
            for (int i = 0; i < poids.Length; i++)
            {
                if (poids[i] > 0f) total += poids[i];
            }
            if (total <= 0f) return -1;

            float cible = Float01() * total;
            float cumul = 0f;
            for (int i = 0; i < poids.Length; i++)
            {
                if (poids[i] <= 0f) continue;
                cumul += poids[i];
                if (cible < cumul) return i;
            }
            // Repli si l'accumulation flottante tombe juste sous la cible.
            for (int i = poids.Length - 1; i >= 0; i--)
            {
                if (poids[i] > 0f) return i;
            }
            return -1;
        }

        static uint RotationGauche(uint x, int k)
        {
            unchecked
            {
                return (x << k) | (x >> (32 - k));
            }
        }

        /// <summary>Mélangeur SplitMix64 : sert à étaler une graine sur l'état de xoshiro.</summary>
        internal static ulong SplitMix64(ref ulong etat)
        {
            unchecked
            {
                ulong z = (etat += Phi);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
