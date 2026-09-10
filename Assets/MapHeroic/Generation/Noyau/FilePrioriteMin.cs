using System;

namespace MapHeroic.Generation.Noyau
{
    /// <summary>
    /// Tas binaire minimal sur des couples (clé flottante, valeur entière).
    ///
    /// Les égalités de clé sont départagées par la valeur, ce qui rend l'ordre de sortie
    /// total : deux exécutions dépilent exactement la même suite. C'est indispensable pour le
    /// comblement des dépressions, où les plateaux produisent quantité de clés identiques —
    /// un tas qui les sortirait dans un ordre arbitraire donnerait un relief différent à
    /// chaque exécution.
    /// </summary>
    public sealed class FilePrioriteMin
    {
        float[] _cles;
        int[] _valeurs;
        int _taille;

        public FilePrioriteMin(int capacite = 64)
        {
            if (capacite < 1) capacite = 1;
            _cles = new float[capacite];
            _valeurs = new int[capacite];
        }

        public int Taille => _taille;
        public bool EstVide => _taille == 0;

        public void Vider() => _taille = 0;

        public void Empiler(float cle, int valeur)
        {
            if (_taille == _cles.Length)
            {
                Array.Resize(ref _cles, _cles.Length * 2);
                Array.Resize(ref _valeurs, _valeurs.Length * 2);
            }

            int i = _taille++;
            _cles[i] = cle;
            _valeurs[i] = valeur;

            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!EstAvant(i, parent)) break;
                Echanger(i, parent);
                i = parent;
            }
        }

        public bool Depiler(out float cle, out int valeur)
        {
            if (_taille == 0)
            {
                cle = 0f;
                valeur = -1;
                return false;
            }

            cle = _cles[0];
            valeur = _valeurs[0];

            _taille--;
            if (_taille > 0)
            {
                _cles[0] = _cles[_taille];
                _valeurs[0] = _valeurs[_taille];

                int i = 0;
                while (true)
                {
                    int gauche = 2 * i + 1;
                    if (gauche >= _taille) break;
                    int droite = gauche + 1;
                    int petit = (droite < _taille && EstAvant(droite, gauche)) ? droite : gauche;
                    if (!EstAvant(petit, i)) break;
                    Echanger(i, petit);
                    i = petit;
                }
            }
            return true;
        }

        bool EstAvant(int a, int b)
        {
            if (_cles[a] != _cles[b]) return _cles[a] < _cles[b];
            return _valeurs[a] < _valeurs[b];
        }

        void Echanger(int a, int b)
        {
            float ck = _cles[a]; _cles[a] = _cles[b]; _cles[b] = ck;
            int cv = _valeurs[a]; _valeurs[a] = _valeurs[b]; _valeurs[b] = cv;
        }
    }
}
