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

        /// <summary>Par arête fine : traversée par un lit de rivière.</summary>
        public bool[] AreteRiviere;

        /// <summary>Chaînes de coins, de la source à l'embouchure.</summary>
        public List<int[]> Rivieres = new List<int[]>();

        /// <summary>Dépressions comblées assez petites pour devenir des lacs en P8.</summary>
        public List<int[]> BassinsCandidats = new List<int[]>();

        // ----------------------------------------------------------------- P5 : zones

        /// <summary>Par cellule : index de zone, ou -1 en mer.</summary>
        public int[] ZoneDeCellule;

        /// <summary>Cellules de chaque zone.</summary>
        public List<int>[] CellulesDeZone;

        /// <summary>Graphe des zones : voisines de chacune, triées.</summary>
        public List<int>[] ZonesVoisines;

        /// <summary>
        /// Frontières entre zones voisines, avec leurs arêtes fines et leurs grandeurs
        /// agrégées. C'est sur elles que raisonne la phase des groupes : décider si une
        /// frontière est un bon endroit où séparer deux groupes demande de connaître sa
        /// longueur, sa dureté et si une rivière la suit — pas de reparcourir les cellules.
        /// </summary>
        public AreteZones[] AretesEntreZones;

        /// <summary>Par zone : index de ses frontières dans <see cref="AretesEntreZones"/>.</summary>
        public int[][] AretesDeZone;

        public int NbZones => CellulesDeZone?.Length ?? 0;

        // --------------------------------------------------------------- P6 : groupes

        /// <summary>Par zone : index de son groupe.</summary>
        public int[] GroupeDeZone;

        /// <summary>Zones de chaque groupe (3 ou 4).</summary>
        public List<int>[] ZonesDeGroupe;

        /// <summary>Graphe des groupes : voisins de chacun, triés.</summary>
        public List<int>[] GroupesVoisins;

        public int NbGroupes => ZonesDeGroupe?.Length ?? 0;

        // ------------------------------------------------------------- P7 : passages

        /// <summary>Frontières entre groupes, chaînées et évaluées.</summary>
        public FrontiereGroupes[] FrontieresGroupes;

        /// <summary>Ouvertures praticables entre groupes.</summary>
        public List<Passage> Passages = new List<Passage>();

        /// <summary>
        /// Par cellule : réservée par un passage. Aucune phase suivante n'y posera de massif,
        /// de lac ni de lit de rivière — c'est ce qui garantit qu'un col ouvert le reste.
        /// </summary>
        public bool[] CelluleReservee;

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
