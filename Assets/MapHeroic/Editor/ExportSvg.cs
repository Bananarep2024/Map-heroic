using System;
using System.Globalization;
using System.IO;
using System.Text;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MapHeroic.EditeurCarte
{
    /// <summary>
    /// Export d'une carte en SVG.
    ///
    /// Le format vectoriel permet de zoomer sans perdre le tracé des frontières, ce qui est
    /// exactement ce qu'on veut inspecter. Et il s'ouvre partout, sans Unity — utile pour
    /// montrer une carte, l'annoter ou la comparer à une autre.
    /// </summary>
    public static class ExportSvg
    {
        const float Cote = 1400f;
        const float Marge = 24f;
        const float HauteurLegende = 132f;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [MenuItem("Map Heroic/Exporter la carte en SVG")]
        public static void ExporterDepuisMenu()
        {
            string chemin = EditorUtility.SaveFilePanel("Exporter la carte", "", "carte.svg", "svg");
            if (string.IsNullOrEmpty(chemin)) return;
            Exporter(20260910UL, new ParametresGeneration(), chemin);
            EditorUtility.RevealInFinder(chemin);
        }

        /// <summary>
        /// Point d'entrée en ligne de commande :
        /// <c>-executeMethod MapHeroic.EditeurCarte.ExportSvg.DepuisLigneDeCommande -graine N -sortie chemin.svg</c>
        /// </summary>
        public static void DepuisLigneDeCommande()
        {
            ulong graine = 20260910UL;
            string sortie = Path.Combine(Path.GetTempPath(), "carte.svg");

            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-graine") ulong.TryParse(arguments[i + 1], out graine);
                else if (arguments[i] == "-sortie") sortie = arguments[i + 1];
            }

            bool ok = Exporter(graine, new ParametresGeneration(), sortie);
            Debug.Log(ok ? $"[SVG] écrit : {sortie}" : $"[SVG] échec pour la graine {graine}");
            EditorApplication.Exit(ok ? 0 : 2);
        }

        public static bool Exporter(ulong graine, ParametresGeneration parametres, string chemin)
        {
            var carte = GenerateurCarte.Generer(graine, parametres, out RapportGeneration rapport);
            if (carte == null) return false;

            GrapheCellules g = carte.Graphe;
            float largeur = Cote + Marge * 2f;
            float hauteur = Cote + Marge * 2f + HauteurLegende;

            var svg = new StringBuilder(4 * 1024 * 1024);
            svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(largeur)} {N(hauteur)}\" ")
               .Append($"width=\"{N(largeur)}\" height=\"{N(hauteur)}\">\n");
            svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#12151a\"/>\n");
            svg.Append($"<rect x=\"{N(Marge)}\" y=\"{N(Marge)}\" width=\"{N(Cote)}\" height=\"{N(Cote)}\" fill=\"#20344f\"/>\n");

            // --- cellules ---------------------------------------------------------
            svg.Append("<g stroke=\"none\">\n");
            for (int c = 0; c < g.NbCellules; c++)
            {
                svg.Append($"<path fill=\"{Hex(CouleurCellule(carte, c))}\" d=\"{Contour(g, c)}\"/>\n");
            }
            svg.Append("</g>\n");

            // --- cours d'eau ------------------------------------------------------
            svg.Append(Aretes(g, e => carte.TypeArete[e] == TypeFrontiere.RiviereProlongee, "#4f92c4", 2.2f));
            svg.Append(Aretes(g, e => carte.TypeArete[e] == TypeFrontiere.Riviere, "#3d84bf", 4.5f));

            // --- frontières -------------------------------------------------------
            svg.Append(Aretes(g, e => SepareDeuxZones(carte, g, e, false), "#00000055", 1.1f));
            svg.Append(Aretes(g, e => SepareDeuxZones(carte, g, e, true), "#150d07", 3.4f));

            // --- passages ---------------------------------------------------------
            var aretesPassage = new System.Collections.Generic.HashSet<int>();
            foreach (Passage passage in carte.Passages)
            {
                foreach (int e in passage.AretesFines) aretesPassage.Add(e);
            }
            svg.Append(Aretes(g, e => aretesPassage.Contains(e), "#3ad84a", 5f));

            // --- ponts, sockets, départs -----------------------------------------
            svg.Append("<g>\n");
            foreach (EmplacementPont pont in carte.Ponts)
            {
                svg.Append(Cercle(pont.Position, 4f, "#f0a020", null));
            }
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (carte.ADrapeau(c, DrapeauxCellule.SocketMine)) svg.Append(Cercle(g.Sites[c], 4.5f, "#d98a2b", "#2a1a08"));
            }
            foreach (int zone in carte.ZonesDepart)
            {
                if (zone < 0) continue;
                float2 centre = float2.zero;
                float poids = 0f;
                foreach (int cellule in carte.CellulesDeZone[zone])
                {
                    float a = g.Aire[cellule];
                    centre += g.Sites[cellule] * a;
                    poids += a;
                }
                if (poids > 0f) svg.Append(Etoile(centre / poids, 16f));
            }
            svg.Append("</g>\n");

            svg.Append(Legende(carte, rapport, graine, largeur));
            svg.Append("</svg>\n");

            Directory.CreateDirectory(Path.GetDirectoryName(chemin) ?? ".");
            File.WriteAllText(chemin, svg.ToString(), new UTF8Encoding(false));
            return true;
        }

        // --------------------------------------------------------------- éléments

        static string Contour(GrapheCellules g, int cellule)
        {
            var d = new StringBuilder(96);
            int debut = g.DebutCoins[cellule];
            for (int s = debut; s < g.DebutCoins[cellule + 1]; s++)
            {
                float2 point = Projeter(g.Coins[g.CoinsDeCellule[s]]);
                d.Append(s == debut ? "M" : "L").Append(N(point.x)).Append(' ').Append(N(point.y));
            }
            d.Append('Z');
            return d.ToString();
        }

        static string Aretes(GrapheCellules g, Func<int, bool> filtre, string couleur, float epaisseur)
        {
            var d = new StringBuilder(64 * 1024);
            for (int e = 0; e < g.NbAretes; e++)
            {
                if (!filtre(e)) continue;
                float2 a = Projeter(g.Coins[g.AreteCoinA[e]]);
                float2 b = Projeter(g.Coins[g.AreteCoinB[e]]);
                d.Append('M').Append(N(a.x)).Append(' ').Append(N(a.y))
                 .Append('L').Append(N(b.x)).Append(' ').Append(N(b.y));
            }
            if (d.Length == 0) return "";
            return $"<path d=\"{d}\" stroke=\"{couleur}\" stroke-width=\"{N(epaisseur)}\" " +
                   "fill=\"none\" stroke-linecap=\"round\"/>\n";
        }

        static bool SepareDeuxZones(Carte carte, GrapheCellules g, int arete, bool groupes)
        {
            int a = g.AreteCelluleA[arete], b = g.AreteCelluleB[arete];
            if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) return false;
            int za = carte.ZoneDeCellule[a], zb = carte.ZoneDeCellule[b];
            if (za < 0 || zb < 0) return false;
            return groupes ? carte.GroupeDeZone[za] != carte.GroupeDeZone[zb] : za != zb;
        }

        static string Cercle(float2 position, float rayon, string remplissage, string contour)
        {
            float2 p = Projeter(position);
            string trait = contour != null ? $" stroke=\"{contour}\" stroke-width=\"1.4\"" : "";
            return $"<circle cx=\"{N(p.x)}\" cy=\"{N(p.y)}\" r=\"{N(rayon)}\" fill=\"{remplissage}\"{trait}/>\n";
        }

        static string Etoile(float2 position, float rayon)
        {
            float2 c = Projeter(position);
            var d = new StringBuilder(160);
            for (int i = 0; i < 10; i++)
            {
                float angle = (float)(-Math.PI / 2 + i * Math.PI / 5);
                float r = i % 2 == 0 ? rayon : rayon * 0.42f;
                float x = c.x + r * (float)Math.Cos(angle);
                float y = c.y + r * (float)Math.Sin(angle);
                d.Append(i == 0 ? "M" : "L").Append(N(x)).Append(' ').Append(N(y));
            }
            d.Append('Z');
            return $"<path d=\"{d}\" fill=\"#ffe14d\" stroke=\"#241a00\" stroke-width=\"2\"/>\n";
        }

        static string Legende(Carte carte, RapportGeneration rapport, ulong graine, float largeur)
        {
            float y = Marge + Cote + 30f;
            var svg = new StringBuilder(4096);
            svg.Append("<g font-family=\"Segoe UI, Helvetica, Arial, sans-serif\">\n");
            svg.Append($"<text x=\"{N(Marge)}\" y=\"{N(y)}\" fill=\"#e8eaed\" font-size=\"21\" " +
                       $"font-weight=\"600\">Map Heroic — graine {graine}</text>\n");

            string stats = $"{carte.NbCellules} cellules · {carte.NbZones} zones · {carte.NbGroupes} groupes · " +
                           $"{carte.Passages.Count} passages · {carte.Ponts.Count} ponts · " +
                           $"{rapport.MillisecondesTotal} ms";
            svg.Append($"<text x=\"{N(largeur - Marge)}\" y=\"{N(y)}\" fill=\"#9aa4b2\" font-size=\"15\" " +
                       $"text-anchor=\"end\">{stats}</text>\n");

            var entrees = new (string nom, string couleur)[]
            {
                ("plaine fertile", Hex(PaletteCarte.Terrain(TypeTerrain.PlaineFertile))),
                ("plaine argileuse", Hex(PaletteCarte.Terrain(TypeTerrain.PlaineArgileuse))),
                ("forêt", Hex(PaletteCarte.Terrain(TypeTerrain.Foret))),
                ("colline", Hex(PaletteCarte.Terrain(TypeTerrain.Colline))),
                ("montagne", Hex(PaletteCarte.Terrain(TypeTerrain.Montagne))),
                ("désert", Hex(PaletteCarte.Terrain(TypeTerrain.Desert))),
                ("marais", Hex(PaletteCarte.Terrain(TypeTerrain.Marais))),
                ("côte", Hex(PaletteCarte.Terrain(TypeTerrain.Cote))),
                ("massif", Hex(PaletteCarte.Massif)),
                ("lac", Hex(PaletteCarte.Lac))
            };

            float x = Marge;
            float ligne = y + 30f;
            foreach (var (nom, couleur) in entrees)
            {
                svg.Append($"<rect x=\"{N(x)}\" y=\"{N(ligne - 12)}\" width=\"16\" height=\"16\" rx=\"3\" fill=\"{couleur}\"/>\n");
                svg.Append($"<text x=\"{N(x + 23)}\" y=\"{N(ligne)}\" fill=\"#c8cfd8\" font-size=\"15\">{nom}</text>\n");
                x += 23 + nom.Length * 8.2f + 22;
            }

            float ligne2 = ligne + 32f;
            svg.Append(EntreeTrait(Marge, ligne2, "#150d07", 3.4f, "frontière de groupe"));
            svg.Append(EntreeTrait(Marge + 240, ligne2, "#3ad84a", 5f, "passage"));
            svg.Append(EntreeTrait(Marge + 400, ligne2, "#3d84bf", 4.5f, "rivière"));
            svg.Append(EntreeTrait(Marge + 560, ligne2, "#4f92c4", 2.2f, "ruisseau de séparation"));
            svg.Append($"<circle cx=\"{N(Marge + 830)}\" cy=\"{N(ligne2 - 5)}\" r=\"4\" fill=\"#f0a020\"/>\n");
            svg.Append($"<text x=\"{N(Marge + 845)}\" y=\"{N(ligne2)}\" fill=\"#c8cfd8\" font-size=\"15\">pont possible</text>\n");
            svg.Append($"<circle cx=\"{N(Marge + 990)}\" cy=\"{N(ligne2 - 5)}\" r=\"4.5\" fill=\"#d98a2b\" stroke=\"#2a1a08\" stroke-width=\"1.4\"/>\n");
            svg.Append($"<text x=\"{N(Marge + 1005)}\" y=\"{N(ligne2)}\" fill=\"#c8cfd8\" font-size=\"15\">mine</text>\n");
            svg.Append($"<path d=\"M{N(Marge + 1090)} {N(ligne2 - 13)}l4 8 9 1-6.5 6 1.5 9-8-4.5-8 4.5 1.5-9-6.5-6 9-1z\" fill=\"#ffe14d\"/>\n");
            svg.Append($"<text x=\"{N(Marge + 1112)}\" y=\"{N(ligne2)}\" fill=\"#c8cfd8\" font-size=\"15\">départ</text>\n");

            svg.Append("</g>\n");
            return svg.ToString();
        }

        static string EntreeTrait(float x, float y, string couleur, float epaisseur, string nom)
        {
            return $"<path d=\"M{N(x)} {N(y - 5)}L{N(x + 26)} {N(y - 5)}\" stroke=\"{couleur}\" " +
                   $"stroke-width=\"{N(epaisseur)}\" stroke-linecap=\"round\"/>\n" +
                   $"<text x=\"{N(x + 34)}\" y=\"{N(y)}\" fill=\"#c8cfd8\" font-size=\"15\">{nom}</text>\n";
        }

        // ----------------------------------------------------------------- couleurs

        static Color CouleurCellule(Carte carte, int cellule)
        {
            if (!carte.Terre[cellule])
            {
                return carte.DistCote[cellule] >= -2 ? PaletteCarte.HautsFonds : PaletteCarte.Mer;
            }
            if (carte.ADrapeau(cellule, DrapeauxCellule.Lac)) return PaletteCarte.Lac;
            if (carte.ADrapeau(cellule, DrapeauxCellule.Massif)) return PaletteCarte.Massif;

            int zone = carte.ZoneDeCellule[cellule];
            return zone >= 0 ? PaletteCarte.Terrain(carte.TerrainDeZone[zone]) : PaletteCarte.Mer;
        }

        static float2 Projeter(float2 point) => new float2(Marge + point.x, Marge + (Cote - point.y));

        static string N(float v) => v.ToString("0.#", Inv);

        static string Hex(Color c)
        {
            return $"#{Mathf.RoundToInt(c.r * 255):x2}{Mathf.RoundToInt(c.g * 255):x2}{Mathf.RoundToInt(c.b * 255):x2}";
        }
    }
}
