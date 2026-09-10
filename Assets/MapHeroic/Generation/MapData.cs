using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using Unity.Mathematics;

namespace MapHeroic.Generation
{
    /// <summary>
    /// Forme figée d'une carte générée : ce que le jeu consomme et ce que le réseau compare.
    ///
    /// Une seule table topologique est conservée — les coins de chaque cellule. Voisins,
    /// arêtes, aires et graphe des coins se reconstruisent à la lecture par une routine
    /// entière déterministe. Cela divise par trois la taille des données et supprime le
    /// risque qu'un tableau dérivé se désynchronise de sa source.
    ///
    /// L'empreinte ne porte que sur la PARTIE DISCRÈTE : appartenances, drapeaux, terrains,
    /// passages, départs. Deux machines peuvent différer d'un dernier bit sur une altitude
    /// sans que la partie soit différente ; hacher les hauteurs brutes ferait échouer la
    /// comparaison pour rien. Les altitudes sont d'ailleurs quantifiées au pas de 5 cm et
    /// disposent de leur propre empreinte, informative et non bloquante.
    /// </summary>
    [Serializable]
    public sealed class MapData
    {
        public ulong Graine;
        public int VersionGenerateur = 1;

        /// <summary>Empreinte de la partie discrète : c'est elle qui fait autorité.</summary>
        public ulong Empreinte;

        /// <summary>Empreinte des altitudes quantifiées, informative.</summary>
        public ulong EmpreinteAltitudes;

        public float TailleCarte;
        public int NbCellules;
        public int NbCoins;

        public float2[] Sites;
        public float2[] Coins;

        /// <summary>Seule topologie sérialisée. Le reste se reconstruit.</summary>
        public int[] CoinsDeCellule;
        public int[] DebutCoins;

        /// <summary>Altitudes au pas de 5 cm (mètres × 20).</summary>
        public short[] AltitudeCoin;

        public ushort[] Drapeaux;
        public short[] ZoneDeCellule;
        public sbyte[] DistCote;

        public short[] GroupeDeZone;
        public TypeTerrain[] TerrainDeZone;
        public TypeRessource[] RessourceDeZone;
        public bool[] ZoneRiveraine;
        public bool[] ZoneMontagne;

        public PassageData[] Passages;
        public PontData[] Ponts;
        public LacSerialise[] Lacs;

        public short[] GroupesDepart;
        public short[] ZonesDepart;

        [Serializable]
        public struct PassageData
        {
            public short GroupeA;
            public short GroupeB;
            public int[] Aretes;
            public int[] Cellules;
            public float Largeur;
            public float2 Position;
        }

        [Serializable]
        public struct PontData
        {
            public short GroupeA;
            public short GroupeB;
            public int[] Aretes;
            public float Largeur;
            public float2 Position;
        }

        [Serializable]
        public struct LacSerialise
        {
            public int[] Cellules;
            public float Niveau;
        }

        public int NbZones => TerrainDeZone?.Length ?? 0;
        public int NbGroupes
        {
            get
            {
                int max = -1;
                if (GroupeDeZone == null) return 0;
                foreach (short g in GroupeDeZone) max = Math.Max(max, g);
                return max + 1;
            }
        }

        // ------------------------------------------------------------ construction

        public static MapData Depuis(Carte carte, float tailleCarte)
        {
            GrapheCellules g = carte.Graphe;
            var data = new MapData
            {
                Graine = carte.Graine,
                TailleCarte = tailleCarte,
                NbCellules = g.NbCellules,
                NbCoins = g.NbCoins,
                Sites = (float2[])g.Sites.Clone(),
                Coins = (float2[])g.Coins.Clone(),
                CoinsDeCellule = (int[])g.CoinsDeCellule.Clone(),
                DebutCoins = (int[])g.DebutCoins.Clone(),
                Drapeaux = (ushort[])carte.Drapeaux.Clone(),
                TerrainDeZone = (TypeTerrain[])carte.TerrainDeZone.Clone(),
                RessourceDeZone = (TypeRessource[])carte.RessourceDeZone.Clone(),
                ZoneRiveraine = (bool[])carte.ZoneRiveraine.Clone(),
                ZoneMontagne = (bool[])carte.ZoneMontagne.Clone()
            };

            data.AltitudeCoin = new short[g.NbCoins];
            for (int i = 0; i < g.NbCoins; i++)
            {
                data.AltitudeCoin[i] = (short)math.clamp((int)math.round(carte.HauteurCoin[i] * 20f), short.MinValue, short.MaxValue);
            }

            data.ZoneDeCellule = new short[g.NbCellules];
            data.DistCote = new sbyte[g.NbCellules];
            for (int c = 0; c < g.NbCellules; c++)
            {
                data.ZoneDeCellule[c] = (short)carte.ZoneDeCellule[c];
                data.DistCote[c] = (sbyte)math.clamp(carte.DistCote[c], -127, 127);
            }

            data.GroupeDeZone = new short[carte.NbZones];
            for (int z = 0; z < carte.NbZones; z++) data.GroupeDeZone[z] = (short)carte.GroupeDeZone[z];

            data.Passages = new PassageData[carte.Passages.Count];
            for (int i = 0; i < carte.Passages.Count; i++)
            {
                Passage passage = carte.Passages[i];
                data.Passages[i] = new PassageData
                {
                    GroupeA = (short)passage.GroupeA,
                    GroupeB = (short)passage.GroupeB,
                    Aretes = (int[])passage.AretesFines.Clone(),
                    Cellules = (int[])passage.Cellules.Clone(),
                    Largeur = passage.Largeur,
                    Position = passage.Position
                };
            }

            data.Ponts = new PontData[carte.Ponts.Count];
            for (int i = 0; i < carte.Ponts.Count; i++)
            {
                EmplacementPont pont = carte.Ponts[i];
                data.Ponts[i] = new PontData
                {
                    GroupeA = (short)pont.GroupeA,
                    GroupeB = (short)pont.GroupeB,
                    Aretes = (int[])pont.Aretes.Clone(),
                    Largeur = pont.Largeur,
                    Position = pont.Position
                };
            }

            data.Lacs = new LacSerialise[carte.Lacs.Count];
            for (int i = 0; i < carte.Lacs.Count; i++)
            {
                data.Lacs[i] = new LacSerialise
                {
                    Cellules = (int[])carte.Lacs[i].Cellules.Clone(),
                    Niveau = carte.Lacs[i].Niveau
                };
            }

            data.GroupesDepart = new short[carte.GroupesDepart.Length];
            data.ZonesDepart = new short[carte.ZonesDepart.Length];
            for (int i = 0; i < carte.GroupesDepart.Length; i++)
            {
                data.GroupesDepart[i] = (short)carte.GroupesDepart[i];
                data.ZonesDepart[i] = (short)carte.ZonesDepart[i];
            }

            data.Empreinte = data.CalculerEmpreinte();
            data.EmpreinteAltitudes = data.CalculerEmpreinteAltitudes();
            return data;
        }

        // ------------------------------------------------------------- empreintes

        public ulong CalculerEmpreinte()
        {
            var h = HashFnv.Nouveau();
            h.Entier(VersionGenerateur);
            h.Entier(NbCellules);
            h.Entier(NbCoins);
            h.Tableau(CoinsDeCellule);
            h.Tableau(DebutCoins);
            h.Tableau(Drapeaux);
            h.Tableau(ZoneDeCellule);
            h.Tableau(GroupeDeZone);

            h.Entier(TerrainDeZone.Length);
            foreach (TypeTerrain t in TerrainDeZone) h.Octet((byte)t);
            foreach (TypeRessource r in RessourceDeZone) h.Octet((byte)r);
            h.Tableau(ZoneRiveraine);
            h.Tableau(ZoneMontagne);

            h.Entier(Passages.Length);
            foreach (PassageData passage in Passages)
            {
                h.Court(passage.GroupeA);
                h.Court(passage.GroupeB);
                h.Tableau(passage.Aretes);
                h.Tableau(passage.Cellules);
            }

            h.Entier(Ponts.Length);
            foreach (PontData pont in Ponts)
            {
                h.Court(pont.GroupeA);
                h.Court(pont.GroupeB);
                h.Tableau(pont.Aretes);
            }

            h.Entier(Lacs.Length);
            foreach (LacSerialise lac in Lacs) h.Tableau(lac.Cellules);

            h.Tableau(GroupesDepart);
            h.Tableau(ZonesDepart);
            return h.Valeur;
        }

        ulong CalculerEmpreinteAltitudes()
        {
            var h = HashFnv.Nouveau();
            h.Tableau(AltitudeCoin);
            return h.Valeur;
        }

        // -------------------------------------------------------------- relecture

        /// <summary>
        /// Reconstruit le maillage complet à partir des seules tables sérialisées, comme le
        /// fera un client qui aura reçu la carte par le réseau.
        /// </summary>
        public GrapheCellules ReconstruireGraphe()
        {
            var g = new GrapheCellules
            {
                NbCellules = NbCellules,
                NbCoins = NbCoins,
                Sites = (float2[])Sites.Clone(),
                Coins = (float2[])Coins.Clone(),
                CoinsDeCellule = (int[])CoinsDeCellule.Clone(),
                DebutCoins = (int[])DebutCoins.Clone()
            };
            g.ReconstruireDerives();
            return g;
        }

        /// <summary>Altitude d'un coin, en mètres.</summary>
        public float Altitude(int coin) => AltitudeCoin[coin] / 20f;

        public bool ADrapeau(int cellule, DrapeauxCellule drapeau)
        {
            return (Drapeaux[cellule] & (ushort)drapeau) != 0;
        }

        /// <summary>Taille approximative en octets, une fois sérialisée sans compression.</summary>
        public long TailleApproximative()
        {
            long total = 0;
            total += (long)Sites.Length * 8 + (long)Coins.Length * 8;
            total += (long)CoinsDeCellule.Length * 4 + (long)DebutCoins.Length * 4;
            total += (long)AltitudeCoin.Length * 2;
            total += (long)Drapeaux.Length * 2 + (long)ZoneDeCellule.Length * 2 + DistCote.Length;
            total += (long)GroupeDeZone.Length * 2 + TerrainDeZone.Length + RessourceDeZone.Length;
            foreach (PassageData passage in Passages) total += 16 + passage.Aretes.Length * 4 + passage.Cellules.Length * 4;
            foreach (PontData pont in Ponts) total += 16 + pont.Aretes.Length * 4;
            foreach (LacSerialise lac in Lacs) total += 8 + lac.Cellules.Length * 4;
            return total;
        }
    }
}
