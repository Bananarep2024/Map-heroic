namespace MapHeroic.Generation.Noyau
{
    /// <summary>
    /// Empreinte FNV-1a 64 bits, utilisée pour signer une carte générée.
    ///
    /// Volontairement sans méthode pour les flottants : l'empreinte qui fait autorité ne
    /// porte que sur la PARTIE DISCRÈTE de la carte (appartenance des cellules aux zones,
    /// groupes, drapeaux, terrains, passages, départs…). Deux machines peuvent différer d'un
    /// dernier bit sur une hauteur sans que la partie soit différente pour autant ; hacher
    /// ces hauteurs brutes ferait échouer la comparaison pour rien. Quand une grandeur
    /// continue doit entrer dans une empreinte, quantifiez-la d'abord en entier (les hauteurs
    /// le sont au pas de 0,05 m, soit un short) puis hachez cet entier.
    ///
    /// Les longueurs de tableau sont hachées avant leur contenu, et un tableau nul se
    /// distingue d'un tableau vide : deux structures différentes ne peuvent pas produire la
    /// même suite d'octets par simple concaténation.
    /// </summary>
    public struct HashFnv
    {
        const ulong Base = 14695981039346656037UL;
        const ulong Premier = 1099511628211UL;

        ulong _h;

        /// <summary>Empreinte neuve, initialisée à la valeur de base FNV.</summary>
        public static HashFnv Nouveau()
        {
            HashFnv h = default;
            h._h = Base;
            return h;
        }

        /// <summary>Valeur courante de l'empreinte.</summary>
        public ulong Valeur => _h;

        public void Octet(byte b)
        {
            unchecked
            {
                _h ^= b;
                _h *= Premier;
            }
        }

        public void Entier32(uint v)
        {
            Octet((byte)v);
            Octet((byte)(v >> 8));
            Octet((byte)(v >> 16));
            Octet((byte)(v >> 24));
        }

        public void Entier64(ulong v)
        {
            Entier32((uint)v);
            Entier32((uint)(v >> 32));
        }

        public void Entier(int v)
        {
            Entier32(unchecked((uint)v));
        }

        public void Court(short v)
        {
            Octet(unchecked((byte)v));
            Octet(unchecked((byte)(v >> 8)));
        }

        public void Booleen(bool v)
        {
            Octet(v ? (byte)1 : (byte)0);
        }

        public void Tableau(int[] t)
        {
            if (t == null) { Entier(-1); return; }
            Entier(t.Length);
            for (int i = 0; i < t.Length; i++) Entier(t[i]);
        }

        public void Tableau(short[] t)
        {
            if (t == null) { Entier(-1); return; }
            Entier(t.Length);
            for (int i = 0; i < t.Length; i++) Court(t[i]);
        }

        public void Tableau(byte[] t)
        {
            if (t == null) { Entier(-1); return; }
            Entier(t.Length);
            for (int i = 0; i < t.Length; i++) Octet(t[i]);
        }

        public void Tableau(ushort[] t)
        {
            if (t == null) { Entier(-1); return; }
            Entier(t.Length);
            for (int i = 0; i < t.Length; i++) Court(unchecked((short)t[i]));
        }

        public void Tableau(bool[] t)
        {
            if (t == null) { Entier(-1); return; }
            Entier(t.Length);
            for (int i = 0; i < t.Length; i++) Booleen(t[i]);
        }

        /// <summary>Empreinte d'un tableau d'entiers en un appel.</summary>
        public static ulong De(int[] t)
        {
            HashFnv h = Nouveau();
            h.Tableau(t);
            return h.Valeur;
        }
    }
}
