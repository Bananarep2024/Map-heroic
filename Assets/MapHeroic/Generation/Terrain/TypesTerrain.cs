using System;

namespace MapHeroic.Generation.Terrain
{
    /// <summary>Nature d'une frontière matérialisée entre deux groupes.</summary>
    public enum TypeFrontiere : byte
    {
        Libre,
        Massif,
        Riviere,
        RiviereProlongee,
        Lac,
        Mer,
        Col,
        Gue,
        BandeCotiere
    }

    /// <summary>Attributs d'une cellule, cumulables.</summary>
    [Flags]
    public enum DrapeauxCellule : ushort
    {
        Aucun = 0,
        Terre = 1,
        Plage = 2,
        Massif = 4,
        PiedMassif = 8,
        Lac = 16,
        RiveLac = 32,
        NonConstructible = 64,
        SocketMine = 128,
        SocketPeche = 256,

        /// <summary>Cellule d'un passage : aucun obstacle ne peut y être posé.</summary>
        Reserve = 512
    }

    /// <summary>Type de terrain d'une zone. Une zone n'en a qu'un.</summary>
    public enum TypeTerrain : byte
    {
        PlaineFertile,
        PlaineArgileuse,
        Foret,
        Colline,
        Montagne,
        Desert,
        Marais,
        Cote
    }

    /// <summary>Ressource produite par une zone.</summary>
    public enum TypeRessource : byte
    {
        Aucune,
        Nourriture,
        Argile,
        Bois,
        Pierre,
        Minerai
    }

    public static class Terrains
    {
        /// <summary>
        /// Ressource de surface d'un terrain. Le poisson n'y figure pas : il ne dépend pas du
        /// terrain mais de la riveraineté, une zone pouvant produire à la fois sa ressource
        /// propre et du poisson.
        /// </summary>
        public static TypeRessource Ressource(TypeTerrain terrain)
        {
            switch (terrain)
            {
                case TypeTerrain.PlaineFertile: return TypeRessource.Nourriture;
                case TypeTerrain.PlaineArgileuse: return TypeRessource.Argile;
                case TypeTerrain.Foret: return TypeRessource.Bois;
                case TypeTerrain.Colline: return TypeRessource.Pierre;
                case TypeTerrain.Montagne: return TypeRessource.Minerai;
                default: return TypeRessource.Aucune;   // désert, marais, côte
            }
        }

        public static string Nom(TypeTerrain terrain)
        {
            switch (terrain)
            {
                case TypeTerrain.PlaineFertile: return "plaine fertile";
                case TypeTerrain.PlaineArgileuse: return "plaine argileuse";
                case TypeTerrain.Foret: return "forêt";
                case TypeTerrain.Colline: return "colline";
                case TypeTerrain.Montagne: return "montagne";
                case TypeTerrain.Desert: return "désert";
                case TypeTerrain.Marais: return "marais";
                case TypeTerrain.Cote: return "côte";
                default: return "?";
            }
        }
    }
}
