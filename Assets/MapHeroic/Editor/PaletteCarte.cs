using MapHeroic.Generation.Terrain;
using UnityEngine;

namespace MapHeroic.EditeurCarte
{
    /// <summary>
    /// Couleurs de l'aperçu 2D.
    ///
    /// Les teintes sont volontairement éloignées les unes des autres : l'aperçu sert à
    /// vérifier un découpage de quatre-vingt-dix zones d'un coup d'œil, pas à préfigurer le
    /// rendu du jeu. Argile en terracotta, désert en jaune pâle, côte en sable blanchi — les
    /// trois qui se confondraient le plus si on les laissait dans les mêmes ocres.
    /// </summary>
    public static class PaletteCarte
    {
        public static readonly Color Mer = new Color(0.13f, 0.27f, 0.42f);
        public static readonly Color HautsFonds = new Color(0.20f, 0.40f, 0.56f);
        public static readonly Color Fond = new Color(0.10f, 0.11f, 0.13f);

        public static readonly Color ContourZone = new Color(0f, 0f, 0f, 0.35f);
        public static readonly Color ContourGroupe = new Color(0.08f, 0.05f, 0.03f, 0.95f);
        public static readonly Color Massif = new Color(0.45f, 0.42f, 0.40f);
        public static readonly Color NeigeMassif = new Color(0.85f, 0.86f, 0.88f);
        public static readonly Color Lac = new Color(0.25f, 0.50f, 0.68f);
        public static readonly Color Riviere = new Color(0.28f, 0.55f, 0.75f);
        public static readonly Color RiviereProlongee = new Color(0.38f, 0.62f, 0.78f);
        public static readonly Color Passage = new Color(0.30f, 0.85f, 0.35f);
        public static readonly Color CelluleReservee = new Color(0.30f, 0.85f, 0.35f, 0.22f);
        public static readonly Color Pont = new Color(0.95f, 0.60f, 0.15f);
        public static readonly Color Depart = new Color(1f, 0.95f, 0.35f);
        public static readonly Color SocketMine = new Color(0.85f, 0.55f, 0.20f);
        public static readonly Color SocketPeche = new Color(0.45f, 0.80f, 0.85f);

        public static Color Terrain(TypeTerrain terrain)
        {
            switch (terrain)
            {
                case TypeTerrain.PlaineFertile: return new Color(0.55f, 0.72f, 0.35f);
                case TypeTerrain.PlaineArgileuse: return new Color(0.71f, 0.34f, 0.23f);
                case TypeTerrain.Foret: return new Color(0.20f, 0.42f, 0.24f);
                case TypeTerrain.Colline: return new Color(0.62f, 0.66f, 0.32f);
                case TypeTerrain.Montagne: return new Color(0.48f, 0.50f, 0.48f);
                case TypeTerrain.Desert: return new Color(0.91f, 0.85f, 0.63f);
                case TypeTerrain.Marais: return new Color(0.36f, 0.38f, 0.26f);
                case TypeTerrain.Cote: return new Color(0.95f, 0.92f, 0.83f);
                default: return Color.magenta;
            }
        }

        /// <summary>Teinte stable par zone : sert au calque « découpage » avant l'affectation des terrains.</summary>
        public static Color Zone(int zone)
        {
            float teinte = (zone * 0.6180339887f) % 1f;
            return Color.HSVToRGB(teinte, 0.45f, 0.80f);
        }

        /// <summary>Teinte stable par groupe, plus saturée pour se distinguer des zones.</summary>
        public static Color Groupe(int groupe)
        {
            float teinte = (groupe * 0.6180339887f + 0.17f) % 1f;
            return Color.HSVToRGB(teinte, 0.70f, 0.85f);
        }

        /// <summary>Dégradé de l'altitude, du rivage au sommet.</summary>
        public static Color Altitude(float metres, float maximum)
        {
            if (metres <= 0f) return Color.Lerp(Mer, HautsFonds, Mathf.Clamp01(1f + metres / 20f));
            float t = Mathf.Clamp01(metres / Mathf.Max(1f, maximum));
            return Color.Lerp(new Color(0.85f, 0.83f, 0.66f), new Color(0.42f, 0.31f, 0.24f), t);
        }
    }
}
