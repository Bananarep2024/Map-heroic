using System.Collections.Generic;
using MapHeroic.Generation.Noyau;

namespace MapHeroic.Generation.Terrain
{
    /// <summary>
    /// État de la carte en cours de génération : chaque phase y ajoute ses tableaux sans
    /// jamais réécrire ceux des précédentes. Ce n'est pas encore <c>MapData</c>, qui sera la
    /// forme figée et sérialisable produite en fin de pipeline.
    ///
    /// Tout est en tableaux parallèles indexés par cellule, par coin ou par arête, jamais en
    /// objets par cellule : les parcours restent ordonnés, donc reproductibles.
    /// </summary>
    public sealed class Carte
    {
        public ulong Graine;
        public GrapheCellules Graphe;

        // ---------------------------------------------------------------- P3 : île

        /// <summary>Par cellule : terre ou mer.</summary>
        public bool[] Terre;

        /// <summary>
        /// Par cellule, en nombre de cellules : ≥ 0 côté terre (0 = cellule de plage),
        /// &lt; 0 côté mer (-1 = mer bordant la terre). Le signe porte l'information
        /// « terre ou mer », la valeur absolue la distance au rivage.
        /// </summary>
        public int[] DistCote;

        /// <summary>Poches de mer refermées par les terres, devenues terre en P3.</summary>
        public List<int[]> PochesInterieures = new List<int[]>();

        // ------------------------------------------------- P4 : relief et hydrologie

        /// <summary>Par coin, en mètres. Négatif sous le niveau de la mer.</summary>
        public float[] HauteurCoin;

        /// <summary>Par cellule, dans [0, 1] : appartenance à une ligne de crête.</summary>
        public float[] Crete;

        /// <summary>Par coin : nombre de coins drainés, soi-même compris.</summary>
        public float[] Flux;

        /// <summary>Par coin : coin vers lequel l'eau s'écoule, ou -1 si exutoire.</summary>
        public int[] Aval;

        /// <summary>Par coin : appartient au lit d'une rivière retenue.</summary>
        public bool[] CoinRiviere;

        /// <summary>
        /// Par arête fine, dans [0, 1] : coût à franchir. Une rivière ou la mer valent 1,
        /// une arête de plaine tend vers 0. C'est ce champ qui fera épouser aux frontières
        /// de zones le relief déjà présent, au lieu de les y plaquer après coup.
        /// </summary>
        public float[] Durete;

        /// <summary>Chaînes de coins, de la source à l'embouchure.</summary>
        public List<int[]> Rivieres = new List<int[]>();

        /// <summary>Dépressions comblées assez petites pour devenir des lacs en P8.</summary>
        public List<int[]> BassinsCandidats = new List<int[]>();

        public int NbCellules => Graphe.NbCellules;
        public int NbCoins => Graphe.NbCoins;
        public int NbAretes => Graphe.NbAretes;

        public int NbCellulesTerre
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Terre.Length; i++) { if (Terre[i]) n++; }
                return n;
            }
        }
    }
}
