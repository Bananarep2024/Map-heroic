using System;
using Unity.Mathematics;

namespace MapHeroic.Generation.Noyau
{
    /// <summary>
    /// Maillage de cellules (le diagramme de Voronoï relaxé) en Structure-of-Arrays.
    ///
    /// C'est la structure unique du générateur : elle porte la classification mer/terre, le
    /// relief, les 90 zones, les groupes, les frontières, puis le maillage 3D et les sources
    /// du navmesh. Aucun objet par cellule, aucun dictionnaire : tout passe par des index,
    /// ce qui garde les parcours ordonnés — donc reproductibles — et l'empreinte mémoire basse.
    ///
    /// Une seule table est la source de vérité : <see cref="CoinsDeCellule"/> et
    /// <see cref="DebutCoins"/>, les coins de chaque cellule dans le sens trigonométrique.
    /// Tout le reste (voisins, arêtes, aires, longueurs, graphe des coins) se recalcule par
    /// <see cref="ReconstruireDerives"/> et n'est donc jamais sérialisé — ce qui divise par
    /// trois la taille de MapData et supprime le risque qu'un dérivé se désynchronise.
    ///
    /// Convention d'indexation partagée : le « créneau » <c>DebutCoins[c] + k</c> désigne à la
    /// fois le k-ième coin de la cellule c ET l'arête qui joint ce coin au suivant. Les tables
    /// <see cref="VoisinsDeCellule"/> et <see cref="AretesDeCellule"/> suivent cette même
    /// indexation : le voisin d'indice k est celui qui est de l'autre côté de l'arête k.
    /// </summary>
    public sealed class GrapheCellules
    {
        public int NbCellules;
        public int NbCoins;
        public int NbAretes;

        /// <summary>Centre de chaque cellule (le site du diagramme de Voronoï).</summary>
        public float2[] Sites;

        /// <summary>Sommets des polygones, partagés entre cellules voisines.</summary>
        public float2[] Coins;

        /// <summary>Coins de chaque cellule, sens trigonométrique (CSR).</summary>
        public int[] CoinsDeCellule;

        /// <summary>Décalages CSR, de longueur <see cref="NbCellules"/> + 1.</summary>
        public int[] DebutCoins;

        // ------------------------------------------------------------------ dérivés

        /// <summary>Cellule de l'autre côté de chaque arête, ou -1 au bord du domaine.</summary>
        public int[] VoisinsDeCellule;

        /// <summary>Index d'arête pour chaque créneau.</summary>
        public int[] AretesDeCellule;

        /// <summary>Première cellule d'une arête (la plus petite des deux).</summary>
        public int[] AreteCelluleA;

        /// <summary>Seconde cellule d'une arête, ou -1 si l'arête est au bord.</summary>
        public int[] AreteCelluleB;

        public int[] AreteCoinA;
        public int[] AreteCoinB;

        /// <summary>Graphe des coins (CSR) : sert au calcul hydrologique.</summary>
        public int[] CoinsVoisins;
        public int[] DebutCoinsVoisins;

        public float[] Aire;
        public float[] LongueurArete;

        /// <summary>Nombre total de créneaux (somme des nombres de coins des cellules).</summary>
        public int NbCreneaux => DebutCoins != null && NbCellules > 0 ? DebutCoins[NbCellules] : 0;

        /// <summary>Nombre de coins — et donc d'arêtes — de la cellule.</summary>
        public int NbCoinsDe(int cellule) => DebutCoins[cellule + 1] - DebutCoins[cellule];

        /// <summary>Index de l'arête entre deux cellules, ou -1 si elles ne se touchent pas.</summary>
        public int AreteEntre(int celluleA, int celluleB)
        {
            int deb = DebutCoins[celluleA];
            int fin = DebutCoins[celluleA + 1];
            for (int s = deb; s < fin; s++)
            {
                if (VoisinsDeCellule[s] == celluleB) return AretesDeCellule[s];
            }
            return -1;
        }

        /// <summary>
        /// Recalcule toutes les tables dérivées à partir des seuls coins de cellules.
        /// Déterministe : les arêtes sont numérotées par (cellule A, cellule B, coin A, coin B)
        /// croissants, un ordre total — deux exécutions produisent exactement la même numérotation.
        /// </summary>
        public void ReconstruireDerives()
        {
            Verifier();

            int nbCreneaux = DebutCoins[NbCellules];

            // 1) Une entrée par arête locale : à quelle cellule elle appartient, quels coins elle joint.
            var entreeCellule = new int[nbCreneaux];
            var entreeCoinMin = new int[nbCreneaux];
            var entreeCoinMax = new int[nbCreneaux];

            for (int c = 0; c < NbCellules; c++)
            {
                int deb = DebutCoins[c];
                int n = DebutCoins[c + 1] - deb;
                for (int k = 0; k < n; k++)
                {
                    int a = CoinsDeCellule[deb + k];
                    int b = CoinsDeCellule[deb + (k + 1) % n];
                    int creneau = deb + k;
                    entreeCellule[creneau] = c;
                    entreeCoinMin[creneau] = a < b ? a : b;
                    entreeCoinMax[creneau] = a < b ? b : a;
                }
            }

            // 2) Tri par (paire de coins, cellule). Aucune égalité possible sur un maillage
            //    valide : une cellule ne peut pas porter deux fois la même arête.
            var ordre = new int[nbCreneaux];
            for (int i = 0; i < nbCreneaux; i++) ordre[i] = i;
            Array.Sort(ordre, (x, y) =>
            {
                if (entreeCoinMin[x] != entreeCoinMin[y]) return entreeCoinMin[x] < entreeCoinMin[y] ? -1 : 1;
                if (entreeCoinMax[x] != entreeCoinMax[y]) return entreeCoinMax[x] < entreeCoinMax[y] ? -1 : 1;
                return entreeCellule[x] < entreeCellule[y] ? -1 : (entreeCellule[x] > entreeCellule[y] ? 1 : 0);
            });

            // 3) Deux entrées consécutives de même paire de coins forment une arête intérieure ;
            //    une entrée isolée est une arête de bord.
            var brutCelluleA = new int[nbCreneaux];
            var brutCelluleB = new int[nbCreneaux];
            var brutCoinA = new int[nbCreneaux];
            var brutCoinB = new int[nbCreneaux];
            var brutCreneau0 = new int[nbCreneaux];
            var brutCreneau1 = new int[nbCreneaux];
            int nbAretes = 0;

            int i0 = 0;
            while (i0 < nbCreneaux)
            {
                int premier = ordre[i0];
                int i1 = i0 + 1;
                while (i1 < nbCreneaux
                       && entreeCoinMin[ordre[i1]] == entreeCoinMin[premier]
                       && entreeCoinMax[ordre[i1]] == entreeCoinMax[premier])
                {
                    i1++;
                }

                int compte = i1 - i0;
                if (compte > 2)
                {
                    throw new InvalidOperationException(
                        $"Maillage invalide : l'arête ({entreeCoinMin[premier]}, {entreeCoinMax[premier]}) " +
                        $"est portée par {compte} cellules ; une arête en joint au plus deux.");
                }

                int second = compte == 2 ? ordre[i0 + 1] : -1;
                brutCelluleA[nbAretes] = entreeCellule[premier];
                brutCelluleB[nbAretes] = second >= 0 ? entreeCellule[second] : -1;
                brutCoinA[nbAretes] = entreeCoinMin[premier];
                brutCoinB[nbAretes] = entreeCoinMax[premier];
                brutCreneau0[nbAretes] = premier;
                brutCreneau1[nbAretes] = second;
                nbAretes++;

                i0 = i1;
            }

            // 4) Numérotation définitive des arêtes.
            var ordreAretes = new int[nbAretes];
            for (int i = 0; i < nbAretes; i++) ordreAretes[i] = i;
            Array.Sort(ordreAretes, (x, y) =>
            {
                if (brutCelluleA[x] != brutCelluleA[y]) return brutCelluleA[x] < brutCelluleA[y] ? -1 : 1;
                if (brutCelluleB[x] != brutCelluleB[y]) return brutCelluleB[x] < brutCelluleB[y] ? -1 : 1;
                if (brutCoinA[x] != brutCoinA[y]) return brutCoinA[x] < brutCoinA[y] ? -1 : 1;
                return brutCoinB[x] < brutCoinB[y] ? -1 : (brutCoinB[x] > brutCoinB[y] ? 1 : 0);
            });

            NbAretes = nbAretes;
            AreteCelluleA = new int[nbAretes];
            AreteCelluleB = new int[nbAretes];
            AreteCoinA = new int[nbAretes];
            AreteCoinB = new int[nbAretes];
            LongueurArete = new float[nbAretes];
            VoisinsDeCellule = new int[nbCreneaux];
            AretesDeCellule = new int[nbCreneaux];

            for (int e = 0; e < nbAretes; e++)
            {
                int src = ordreAretes[e];
                int celluleA = brutCelluleA[src];
                int celluleB = brutCelluleB[src];

                AreteCelluleA[e] = celluleA;
                AreteCelluleB[e] = celluleB;
                AreteCoinA[e] = brutCoinA[src];
                AreteCoinB[e] = brutCoinB[src];

                int creneau0 = brutCreneau0[src];
                AretesDeCellule[creneau0] = e;
                VoisinsDeCellule[creneau0] = celluleB;

                int creneau1 = brutCreneau1[src];
                if (creneau1 >= 0)
                {
                    AretesDeCellule[creneau1] = e;
                    VoisinsDeCellule[creneau1] = celluleA;
                }

                float2 pa = Coins[AreteCoinA[e]];
                float2 pb = Coins[AreteCoinB[e]];
                double dx = (double)pb.x - pa.x;
                double dy = (double)pb.y - pa.y;
                LongueurArete[e] = (float)Math.Sqrt(dx * dx + dy * dy);
            }

            CalculerAires();
            ConstruireGrapheCoins();
        }

        void CalculerAires()
        {
            Aire = new float[NbCellules];
            for (int c = 0; c < NbCellules; c++)
            {
                int deb = DebutCoins[c];
                int n = DebutCoins[c + 1] - deb;
                double somme = 0.0;
                for (int k = 0; k < n; k++)
                {
                    float2 p = Coins[CoinsDeCellule[deb + k]];
                    float2 q = Coins[CoinsDeCellule[deb + (k + 1) % n]];
                    somme += (double)p.x * q.y - (double)q.x * p.y;
                }
                Aire[c] = (float)(Math.Abs(somme) * 0.5);
            }
        }

        void ConstruireGrapheCoins()
        {
            var degre = new int[NbCoins];
            for (int e = 0; e < NbAretes; e++)
            {
                degre[AreteCoinA[e]]++;
                degre[AreteCoinB[e]]++;
            }

            DebutCoinsVoisins = new int[NbCoins + 1];
            int cumul = 0;
            for (int i = 0; i < NbCoins; i++)
            {
                DebutCoinsVoisins[i] = cumul;
                cumul += degre[i];
            }
            DebutCoinsVoisins[NbCoins] = cumul;

            CoinsVoisins = new int[cumul];
            var curseur = new int[NbCoins];
            for (int i = 0; i < NbCoins; i++) curseur[i] = DebutCoinsVoisins[i];

            for (int e = 0; e < NbAretes; e++)
            {
                int a = AreteCoinA[e];
                int b = AreteCoinB[e];
                CoinsVoisins[curseur[a]++] = b;
                CoinsVoisins[curseur[b]++] = a;
            }

            // Ordre croissant : le parcours hydrologique dépend de cet ordre, il doit être stable.
            for (int i = 0; i < NbCoins; i++)
            {
                int deb = DebutCoinsVoisins[i];
                Array.Sort(CoinsVoisins, deb, DebutCoinsVoisins[i + 1] - deb);
            }
        }

        void Verifier()
        {
            if (Coins == null) throw new InvalidOperationException("Coins est nul.");
            if (CoinsDeCellule == null) throw new InvalidOperationException("CoinsDeCellule est nul.");
            if (DebutCoins == null) throw new InvalidOperationException("DebutCoins est nul.");
            if (NbCoins != Coins.Length)
                throw new InvalidOperationException($"NbCoins ({NbCoins}) ne correspond pas à Coins.Length ({Coins.Length}).");
            if (DebutCoins.Length != NbCellules + 1)
                throw new InvalidOperationException($"DebutCoins doit être de longueur NbCellules + 1 ({NbCellules + 1}), il fait {DebutCoins.Length}.");
            if (DebutCoins[0] != 0)
                throw new InvalidOperationException("DebutCoins[0] doit valoir 0.");
            if (DebutCoins[NbCellules] != CoinsDeCellule.Length)
                throw new InvalidOperationException("DebutCoins[NbCellules] doit valoir CoinsDeCellule.Length.");

            for (int c = 0; c < NbCellules; c++)
            {
                int n = DebutCoins[c + 1] - DebutCoins[c];
                if (n < 3)
                    throw new InvalidOperationException($"La cellule {c} n'a que {n} coin(s) ; un polygone en demande au moins 3.");
            }
            for (int i = 0; i < CoinsDeCellule.Length; i++)
            {
                int coin = CoinsDeCellule[i];
                if (coin < 0 || coin >= NbCoins)
                    throw new InvalidOperationException($"Index de coin hors plage au créneau {i} : {coin}.");
            }
        }
    }
}
