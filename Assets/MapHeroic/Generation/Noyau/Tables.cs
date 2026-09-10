using System;

namespace MapHeroic.Generation.Noyau
{
    /// <summary>
    /// Sinus, exponentielle décroissante et arc-tangente par tables interpolées.
    ///
    /// Pourquoi ne pas appeler Math.Sin / Math.Exp / Math.Atan2 directement : ces fonctions
    /// sont fournies par la bibliothèque mathématique du système, dont le dernier bit varie
    /// d'une plateforme à l'autre. Une carte dont une décision dépend de ce bit ne serait pas
    /// la même sur Android et sur iOS.
    ///
    /// Les tables elles-mêmes sont donc construites SANS la libm : uniquement + - * / et
    /// racine carrée, opérations dont la norme IEEE-754 impose le résultat exact. Les séries
    /// employées ci-dessous convergent bien au-delà de la précision d'un float.
    ///
    /// Toute entrée non finie rend 0 : aucune conversion float → int hors plage ne peut se
    /// produire (son résultat diffère entre x64 et ARM64).
    /// </summary>
    public static class Tables
    {
        public const float Pi = 3.14159265f;
        public const float DeuxPi = 6.28318531f;
        public const float PiSur2 = 1.57079633f;

        const int TailleSin = 1024;
        const int TailleExp = 256;
        const int TailleAtan = 1024;

        /// <summary>Borne supérieure de la table exponentielle : au-delà, la valeur est plafonnée.</summary>
        public const float ExpMax = 8f;

        static readonly float[] _sin = ConstruireSin();
        static readonly float[] _expNeg = ConstruireExpNeg();
        static readonly float[] _atan = ConstruireAtan();

        // ---------------------------------------------------------------- interface

        /// <summary>Sinus, angle en radians, période gérée.</summary>
        public static float Sin(float angle)
        {
            if (!EstFini(angle)) return 0f;

            float tours = angle * (1f / DeuxPi);
            tours -= (float)Math.Floor(tours);          // ramené dans [0, 1)
            float position = tours * TailleSin;
            int i = (int)position;
            float f = position - i;
            i &= TailleSin - 1;                          // sécurité si position vaut 1024 - epsilon
            int j = (i + 1) & (TailleSin - 1);
            return _sin[i] + (_sin[j] - _sin[i]) * f;
        }

        /// <summary>Cosinus, angle en radians.</summary>
        public static float Cos(float angle)
        {
            return Sin(angle + PiSur2);
        }

        /// <summary>
        /// exp(-x) pour x ≥ 0. Vaut 1 en 0 ; au-delà de <see cref="ExpMax"/> la valeur est
        /// plafonnée à exp(-8) ≈ 3,4e-4 plutôt que brutalement mise à zéro : le profil
        /// d'élévation reste continu et saturant, ce qui est justement l'effet recherché.
        /// </summary>
        public static float ExpNeg(float x)
        {
            if (!EstFini(x)) return 0f;
            if (x <= 0f) return 1f;
            if (x >= ExpMax) return _expNeg[TailleExp];

            float position = x * (TailleExp / ExpMax);
            int i = (int)position;
            if (i >= TailleExp) return _expNeg[TailleExp];
            float f = position - i;
            return _expNeg[i] + (_expNeg[i + 1] - _expNeg[i]) * f;
        }

        /// <summary>Arc-tangente à deux arguments, résultat dans (-π, π]. Rend 0 en (0, 0).</summary>
        public static float Atan2(float y, float x)
        {
            if (!EstFini(x) || !EstFini(y)) return 0f;
            if (x == 0f && y == 0f) return 0f;

            float ax = x < 0f ? -x : x;
            float ay = y < 0f ? -y : y;

            // On ramène toujours le rapport dans [0, 1] pour n'utiliser qu'un octant de table.
            float angle = ax >= ay ? Atan01(ay / ax) : PiSur2 - Atan01(ax / ay);

            if (x < 0f) angle = Pi - angle;
            if (y < 0f) angle = -angle;
            return angle;
        }

        static float Atan01(float t)
        {
            float position = t * TailleAtan;
            int i = (int)position;
            if (i >= TailleAtan) return _atan[TailleAtan];
            if (i < 0) return _atan[0];
            float f = position - i;
            return _atan[i] + (_atan[i + 1] - _atan[i]) * f;
        }

        static bool EstFini(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        // ------------------------------------------------- construction des tables
        // Tout ce qui suit ne tourne qu'une fois, au premier accès à la classe.

        static float[] ConstruireSin()
        {
            var table = new float[TailleSin];
            for (int i = 0; i < TailleSin; i++)
            {
                table[i] = (float)SinExact(2.0 * Math.PI * i / TailleSin);
            }
            return table;
        }

        static float[] ConstruireExpNeg()
        {
            var table = new float[TailleExp + 1];
            for (int i = 0; i <= TailleExp; i++)
            {
                table[i] = (float)ExpNegExact((double)ExpMax * i / TailleExp);
            }
            return table;
        }

        static float[] ConstruireAtan()
        {
            var table = new float[TailleAtan + 1];
            for (int i = 0; i <= TailleAtan; i++)
            {
                table[i] = (float)AtanExact((double)i / TailleAtan);
            }
            return table;
        }

        /// <summary>
        /// sin(x) par série de Taylor après réduction dans [0, π/2].
        /// Termes jusqu'à x^17 : erreur ≈ 6e-14, très en dessous du float.
        /// </summary>
        static double SinExact(double x)
        {
            const double deuxPi = 2.0 * Math.PI;
            x -= deuxPi * Math.Floor(x / deuxPi);

            double signe = 1.0;
            if (x > Math.PI) { x -= Math.PI; signe = -1.0; }   // sin(π + u) = -sin(u)
            if (x > Math.PI / 2.0) x = Math.PI - x;            // sin(π - u) = sin(u)

            double x2 = x * x;
            double terme = x;
            double somme = x;
            for (int n = 1; n <= 8; n++)
            {
                terme *= -x2 / ((2.0 * n) * (2.0 * n + 1.0));
                somme += terme;
            }
            return signe * somme;
        }

        /// <summary>exp(t) pour t dans [0, 1] : termes tous positifs, donc aucune annulation.</summary>
        static double ExpUnitaire(double t)
        {
            double terme = 1.0;
            double somme = 1.0;
            for (int k = 1; k <= 20; k++)
            {
                terme *= t / k;
                somme += terme;
            }
            return somme;
        }

        /// <summary>exp(-x) pour x dans [0, 8], par partie entière et partie fractionnaire.</summary>
        static double ExpNegExact(double x)
        {
            int n = (int)x;                       // x est borné à [0, 8] : conversion sûre
            double f = x - n;
            double resultat = ExpUnitaire(f);
            double e = ExpUnitaire(1.0);
            for (int k = 0; k < n; k++) resultat *= e;
            return 1.0 / resultat;
        }

        /// <summary>
        /// atan(t) pour t dans [0, 1]. La série de Taylor converge mal près de 1 ; on réduit
        /// d'abord par atan(t) = π/6 + atan((t√3 − 1) / (√3 + t)), qui ramène l'argument dans
        /// [-tan(π/12), tan(π/12)] où 25 termes donnent ≈ 4e-16.
        /// </summary>
        static double AtanExact(double t)
        {
            const double tanPiSur12 = 0.26794919243112270647; // 2 − √3
            double decalage = 0.0;
            if (t > tanPiSur12)
            {
                double r3 = Math.Sqrt(3.0);                    // sqrt est exacte en IEEE-754
                t = (t * r3 - 1.0) / (r3 + t);
                decalage = Math.PI / 6.0;
            }

            double t2 = t * t;
            double terme = t;
            double somme = t;
            for (int k = 1; k <= 12; k++)
            {
                terme *= -t2;
                somme += terme / (2.0 * k + 1.0);
            }
            return decalage + somme;
        }
    }
}
