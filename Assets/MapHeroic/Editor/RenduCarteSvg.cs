// Rendu « carte dessinée » : chaque terrain a sa texture — conifères, tufs d'herbe, dunes,
// roseaux, vagues — au lieu d'un aplat de couleur. Les motifs sont vectoriels et définis une
// seule fois, donc le fichier reste léger malgré les six mille cellules.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using Unity.Mathematics;
using UnityEditor;

namespace MapHeroic.EditeurCarte
{
    /// <summary>
    /// Rendu « carte dessinée », par opposition à <see cref="ExportSvg"/> qui est une vue de
    /// débogage : ici chaque terrain porte sa matière et la carte se lit comme une carte.
    /// </summary>
    public static class RenduCarteSvg
    {
        const float Cote = 1400f;
        const float Marge = 34f;
        const float BandeauHaut = 150f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [MenuItem("Map Heroic/Exporter la carte dessinée (SVG)")]
        public static void ExporterDepuisMenu()
        {
            string chemin = EditorUtility.SaveFilePanel("Exporter la carte dessinée", "", "carte.svg", "svg");
            if (string.IsNullOrEmpty(chemin)) return;
            Exporter(20260910UL, chemin);
            EditorUtility.RevealInFinder(chemin);
        }

        /// <summary>
        /// Point d'entrée en ligne de commande :
        /// <c>-executeMethod MapHeroic.EditeurCarte.RenduCarteSvg.DepuisLigneDeCommande -graine N -sortie chemin.svg</c>
        /// </summary>
        public static void DepuisLigneDeCommande()
        {
            string[] args = Environment.GetCommandLineArgs();
            ulong graine = 20260910UL;
            string sortie = Path.Combine(Path.GetTempPath(), "carte.svg");
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-graine") ulong.TryParse(args[i + 1], out graine);
                else if (args[i] == "-sortie") sortie = args[i + 1];
            }
            Exporter(graine, sortie);
        }

        /// <summary>Génère la carte de <paramref name="graine"/> et écrit son SVG.</summary>
        public static bool Exporter(ulong graine, string chemin)
        {
            Carte carte = GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
            if (carte == null)
            {
                UnityEngine.Debug.LogError("Génération échouée : " + rapport.MotifEchec);
                return false;
            }

            File.WriteAllText(chemin, Svg(carte, graine), new UTF8Encoding(false));
            UnityEngine.Debug.Log($"Carte dessinée écrite : {chemin} ({carte.NbZones} zones, {carte.NbGroupes} groupes)");
            return true;
        }

        // ------------------------------------------------------------------- motifs

        /// <summary>
        /// Un motif par terrain. Chaque tuile fait 46 unités pour une cellule d'environ 20 :
        /// on obtient un symbole toutes les deux ou trois cellules, assez pour que la matière se
        /// lise sans noyer le tracé des frontières.
        /// </summary>
        static string Motifs()
        {
            var s = new StringBuilder(8000);
            s.Append("<defs>\n");

            // Conifères
            s.Append(Motif("foret", "#5a7d4a", @"
<path d='M11 30 L18 12 L25 30 Z' fill='#31502c'/><rect x='16.5' y='29' width='3' height='5' fill='#3a2b1c'/>
<path d='M32 40 L37 27 L42 40 Z' fill='#3b5b33'/><rect x='36' y='39' width='2.4' height='4' fill='#3a2b1c'/>
<path d='M4 44 L8 34 L12 44 Z' fill='#3b5b33'/>"));

            // Herbe fertile
            s.Append(Motif("fertile", "#a9c46c", @"
<path d='M10 34 q3 -12 6 0' stroke='#7d9b47' stroke-width='2.2' fill='none' stroke-linecap='round'/>
<path d='M28 22 q3 -11 6 0' stroke='#7d9b47' stroke-width='2.2' fill='none' stroke-linecap='round'/>
<path d='M38 40 q3 -10 6 0' stroke='#7d9b47' stroke-width='2.2' fill='none' stroke-linecap='round'/>
<path d='M2 14 q3 -10 6 0' stroke='#7d9b47' stroke-width='2.2' fill='none' stroke-linecap='round'/>"));

            // Argile : mottes striées
            s.Append(Motif("argile", "#c07a55", @"
<path d='M6 14 h16' stroke='#8f4e30' stroke-width='2.6' stroke-linecap='round'/>
<path d='M24 30 h16' stroke='#8f4e30' stroke-width='2.6' stroke-linecap='round'/>
<path d='M2 38 h12' stroke='#8f4e30' stroke-width='2.6' stroke-linecap='round'/>
<circle cx='34' cy='9' r='2.2' fill='#8f4e30'/><circle cx='14' cy='25' r='2.2' fill='#8f4e30'/>"));

            // Collines : arcs
            s.Append(Motif("colline", "#b9bd72", @"
<path d='M6 28 q9 -12 18 0' stroke='#8b8f4c' stroke-width='2.8' fill='none' stroke-linecap='round'/>
<path d='M26 42 q8 -10 16 0' stroke='#8b8f4c' stroke-width='2.8' fill='none' stroke-linecap='round'/>
<path d='M28 14 q7 -9 14 0' stroke='#8b8f4c' stroke-width='2.8' fill='none' stroke-linecap='round'/>"));

            // Désert : dunes
            s.Append(Motif("desert", "#e8d5a0", @"
<path d='M2 20 q12 -9 22 0 q10 8 20 0' stroke='#c9ab6c' stroke-width='2.6' fill='none' stroke-linecap='round'/>
<path d='M0 38 q12 -8 22 0 q10 7 22 0' stroke='#c9ab6c' stroke-width='2.6' fill='none' stroke-linecap='round'/>
<circle cx='12' cy='30' r='1.8' fill='#c9ab6c'/><circle cx='34' cy='10' r='1.8' fill='#c9ab6c'/>"));

            // Marais : roseaux et flaques
            s.Append(Motif("marais", "#7f8a5c", @"
<path d='M8 34 v-13 M14 36 v-16 M11 35 v-18' stroke='#4d5738' stroke-width='2' stroke-linecap='round'/>
<path d='M32 44 v-12 M37 45 v-15' stroke='#4d5738' stroke-width='2' stroke-linecap='round'/>
<path d='M22 14 q7 -4 13 0' stroke='#5d7f86' stroke-width='3' fill='none' stroke-linecap='round'/>
<path d='M2 44 q6 -3 12 0' stroke='#5d7f86' stroke-width='3' fill='none' stroke-linecap='round'/>"));

            // Côte : sable pointillé
            s.Append(Motif("cote", "#f0e3c2", @"
<circle cx='9' cy='11' r='1.9' fill='#cdb98c'/><circle cx='27' cy='6' r='1.9' fill='#cdb98c'/>
<circle cx='38' cy='19' r='1.9' fill='#cdb98c'/><circle cx='17' cy='26' r='1.9' fill='#cdb98c'/>
<circle cx='31' cy='36' r='1.9' fill='#cdb98c'/><circle cx='6' cy='40' r='1.9' fill='#cdb98c'/>
<circle cx='43' cy='43' r='1.9' fill='#cdb98c'/>"));

            // Montagne : croupes rocheuses
            s.Append(Motif("montagne", "#8c8377", @"
<path d='M4 38 L15 16 L26 38 Z' fill='#6b6155' stroke='#4b433a' stroke-width='1.6'/>
<path d='M15 16 L20 26 L26 38 Z' fill='#5a5147'/>
<path d='M26 44 L34 28 L43 44 Z' fill='#6b6155' stroke='#4b433a' stroke-width='1.6'/>
<path d='M34 28 L38 35 L43 44 Z' fill='#5a5147'/>"));

            // Mer : vagues
            s.Append(Motif("mer", "#8fb6cd", @"
<path d='M0 12 q8 -5 16 0 q8 5 16 0 q8 -5 16 0' stroke='#6f9db8' stroke-width='2.2' fill='none'/>
<path d='M0 30 q8 -5 16 0 q8 5 16 0 q8 -5 16 0' stroke='#6f9db8' stroke-width='2.2' fill='none'/>
<path d='M0 42 q8 -5 16 0 q8 5 16 0 q8 -5 16 0' stroke='#7ba6c0' stroke-width='1.8' fill='none'/>"));

            s.Append(Motif("hautsfonds", "#a7c8d9", @"
<path d='M0 16 q8 -4 16 0 q8 4 16 0 q8 -4 16 0' stroke='#89b0c6' stroke-width='2' fill='none'/>
<path d='M0 36 q8 -4 16 0 q8 4 16 0 q8 -4 16 0' stroke='#89b0c6' stroke-width='2' fill='none'/>"));

            s.Append(Motif("lac", "#7ba7c9", @"
<path d='M0 14 q9 -4 18 0 q9 4 18 0' stroke='#5d8dae' stroke-width='2.2' fill='none'/>
<path d='M0 32 q9 -4 18 0 q9 4 18 0' stroke='#5d8dae' stroke-width='2.2' fill='none'/>"));

            s.Append("</defs>\n");
            return s.ToString();
        }

        static string Motif(string nom, string fond, string contenu)
        {
            return $"<pattern id=\"{nom}\" width=\"46\" height=\"46\" patternUnits=\"userSpaceOnUse\">" +
                   $"<rect width=\"46\" height=\"46\" fill=\"{fond}\"/>{contenu}</pattern>\n";
        }

        static string Texture(Carte carte, int cellule)
        {
            if (!carte.Terre[cellule]) return carte.DistCote[cellule] >= -2 ? "url(#hautsfonds)" : "url(#mer)";
            if (carte.ADrapeau(cellule, DrapeauxCellule.Lac)) return "url(#lac)";
            if (carte.ADrapeau(cellule, DrapeauxCellule.Massif)) return "url(#montagne)";

            int zone = carte.ZoneDeCellule[cellule];
            if (zone < 0) return "url(#mer)";
            switch (carte.TerrainDeZone[zone])
            {
                case TypeTerrain.Foret: return "url(#foret)";
                case TypeTerrain.PlaineFertile: return "url(#fertile)";
                case TypeTerrain.PlaineArgileuse: return "url(#argile)";
                case TypeTerrain.Colline: return "url(#colline)";
                case TypeTerrain.Desert: return "url(#desert)";
                case TypeTerrain.Marais: return "url(#marais)";
                case TypeTerrain.Cote: return "url(#cote)";
                case TypeTerrain.Montagne: return "url(#montagne)";
                default: return "url(#fertile)";
            }
        }

        // -------------------------------------------------------------------- dessin

        static string Svg(Carte carte, ulong graine)
        {
            GrapheCellules g = carte.Graphe;
            CadrerSurIle(carte);
            float largeur = Cote + Marge * 2f;
            float hauteur = Cote + Marge * 2f + BandeauHaut;
            var s = new StringBuilder(8 * 1024 * 1024);

            s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(largeur)} {N(hauteur)}\" width=\"{N(largeur)}\" height=\"{N(hauteur)}\" font-family=\"Segoe UI, Helvetica, Arial, sans-serif\">\n");
            s.Append(Motifs());
            s.Append("<rect width=\"100%\" height=\"100%\" fill=\"#efe6d2\"/>\n");

            // Le zoom fait déborder la mer bien au-delà du cadre : on découpe au carré de carte,
            // sinon elle recouvre le bandeau.
            s.Append($"<clipPath id=\"cadre\"><rect x=\"{N(Marge)}\" y=\"{N(BandeauHaut)}\" width=\"{N(Cote)}\" height=\"{N(Cote)}\"/></clipPath>\n");
            s.Append("<g clip-path=\"url(#cadre)\">\n");

            // Les cellules sont regroupées par texture : une seule bascule de motif par groupe,
            // au lieu d'une par cellule.
            var parTexture = new Dictionary<string, StringBuilder>();
            for (int c = 0; c < g.NbCellules; c++)
            {
                string texture = Texture(carte, c);
                if (!parTexture.TryGetValue(texture, out StringBuilder d))
                {
                    d = new StringBuilder(256 * 1024);
                    parTexture[texture] = d;
                }
                d.Append(Contour(g, c));
            }
            foreach (KeyValuePair<string, StringBuilder> paire in parTexture)
            {
                s.Append($"<path fill=\"{paire.Key}\" d=\"{paire.Value}\"/>\n");
            }

            // Trait de côte, appuyé : c'est lui qui donne sa silhouette à l'île.
            s.Append(Traits(g, e => Rivage(carte, g, e), "#6b5a3e", 5f, 1f));

            // Frontieres internes de zones : discretes, elles ne doivent pas concurrencer les groupes.
            s.Append(Traits(g, e => Separe(carte, g, e, false), "#3f3228", 2f, 0.32f));
            s.Append(Traits(g, e => Separe(carte, g, e, true), "#f3ead6", 12f, 0.85f));
            s.Append(Traits(g, e => Separe(carte, g, e, true), "#2f2820", 6.5f, 1f));

            // Les rivières passent par-dessus : elles servent souvent de frontière de groupe, et le
            // trait noir les masquerait entièrement.
            s.Append(Traits(g, e => carte.TypeArete[e] == TypeFrontiere.RiviereProlongee, "#5f97bd", 6f, 0.95f));
            s.Append(Traits(g, e => carte.TypeArete[e] == TypeFrontiere.Riviere, "#2f6f9e", 11f, 1f));

            var passages = new HashSet<int>();
            foreach (Passage p in carte.Passages)
            {
                foreach (int e in p.AretesFines) passages.Add(e);
            }
            s.Append(Traits(g, e => passages.Contains(e), "#f3ead6", 17f, 1f));
            s.Append(Traits(g, e => passages.Contains(e), "#1c9c37", 9.5f, 1f));

            foreach (EmplacementPont pont in carte.Ponts) s.Append(Losange(pont.Position, 11f));
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (carte.ADrapeau(c, DrapeauxCellule.SocketMine)) s.Append(Pioche(g.Sites[c]));
            }
            for (int i = 0; i < carte.ZonesDepart.Length; i++)
            {
                int zone = carte.ZonesDepart[i];
                if (zone >= 0) s.Append(Banniere(Centre(carte, zone), (i + 1).ToString()));
            }
            s.Append("</g>\n");

            s.Append($"<rect x=\"{N(Marge)}\" y=\"{N(BandeauHaut)}\" width=\"{N(Cote)}\" height=\"{N(Cote)}\" fill=\"none\" stroke=\"#8a7a5c\" stroke-width=\"3\"/>\n");
            s.Append(Bandeau(carte, graine));
            s.Append("</svg>\n");
            return s.ToString();
        }

        static bool Rivage(Carte carte, GrapheCellules g, int e)
        {
            int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
            if (b < 0) return false;
            return carte.Terre[a] != carte.Terre[b];
        }

        static string Bandeau(Carte carte, ulong graine)
        {
            var s = new StringBuilder(3000);
            s.Append($"<text x=\"{N(Marge)}\" y=\"48\" fill=\"#2a2118\" font-size=\"32\" font-weight=\"700\">Map Heroic — graine {graine}</text>\n");
            s.Append($"<text x=\"{N(Marge)}\" y=\"80\" fill=\"#5f5343\" font-size=\"19\">" +
                     $"{carte.NbZones} zones réparties en {carte.NbGroupes} groupes · {carte.Passages.Count} passages · {carte.Ponts.Count} ponts possibles</text>\n");

            float x = Marge, y = 118f;
            x += Trait(s, x, y, "#2f2820", 6.5f, "frontière de groupe") + 30;
            x += Trait(s, x, y, "#1c9c37", 9.5f, "PASSAGE") + 30;
            x += Trait(s, x, y, "#2f6f9e", 11f, "rivière") + 30;
            x += Trait(s, x, y, "#5f97bd", 6f, "ruisseau") + 30;

            s.Append($"<path d=\"M{N(x + 11)} {N(y - 18)}L{N(x + 22)} {N(y - 7)}L{N(x + 11)} {N(y + 4)}L{N(x)} {N(y - 7)}Z\" fill=\"#f3ead6\" stroke=\"#2f2820\" stroke-width=\"2.5\"/>\n");
            s.Append($"<text x=\"{N(x + 32)}\" y=\"{N(y)}\" fill=\"#4a4034\" font-size=\"19\">pont</text>\n");
            x += 32 + 70;

            s.Append($"<path d=\"M{N(x)} {N(y + 2)} l7 -14 l4 2 l-7 14 z M{N(x + 5)} {N(y - 14)} q7 -5 12 2\" fill=\"#2b241d\" stroke=\"#2b241d\" stroke-width=\"3\" stroke-linecap=\"round\"/>\n");
            s.Append($"<text x=\"{N(x + 28)}\" y=\"{N(y)}\" fill=\"#4a4034\" font-size=\"19\">mine</text>\n");
            x += 28 + 78;

            s.Append($"<rect x=\"{N(x)}\" y=\"{N(y - 18)}\" width=\"26\" height=\"22\" rx=\"3\" fill=\"#f3ead6\" stroke=\"#2f2820\" stroke-width=\"2.5\"/>\n");
            s.Append($"<text x=\"{N(x + 13)}\" y=\"{N(y - 2)}\" fill=\"#2a2118\" font-size=\"17\" font-weight=\"700\" text-anchor=\"middle\">3</text>\n");
            s.Append($"<text x=\"{N(x + 36)}\" y=\"{N(y)}\" fill=\"#4a4034\" font-size=\"19\">départ d'un joueur</text>\n");
            return s.ToString();
        }

        static float Trait(StringBuilder s, float x, float y, string couleur, float epaisseur, string nom)
        {
            s.Append($"<path d=\"M{N(x)} {N(y - 6)}L{N(x + 42)} {N(y - 6)}\" stroke=\"{couleur}\" stroke-width=\"{N(epaisseur)}\" stroke-linecap=\"round\"/>\n");
            s.Append($"<text x=\"{N(x + 52)}\" y=\"{N(y)}\" fill=\"#4a4034\" font-size=\"19\">{nom}</text>\n");
            return 52 + nom.Length * 10f;
        }

        static string Contour(GrapheCellules g, int cellule)
        {
            var d = new StringBuilder(96);
            int debut = g.DebutCoins[cellule];
            for (int i = debut; i < g.DebutCoins[cellule + 1]; i++)
            {
                float2 p = Projeter(g.Coins[g.CoinsDeCellule[i]]);
                d.Append(i == debut ? "M" : "L").Append(N(p.x)).Append(' ').Append(N(p.y));
            }
            return d.Append('Z').ToString();
        }

        static string Traits(GrapheCellules g, Func<int, bool> filtre, string couleur, float epaisseur, float opacite)
        {
            if (epaisseur <= 0f) return "";
            var d = new StringBuilder(64 * 1024);
            for (int e = 0; e < g.NbAretes; e++)
            {
                if (!filtre(e)) continue;
                float2 a = Projeter(g.Coins[g.AreteCoinA[e]]);
                float2 b = Projeter(g.Coins[g.AreteCoinB[e]]);
                d.Append('M').Append(N(a.x)).Append(' ').Append(N(a.y)).Append('L').Append(N(b.x)).Append(' ').Append(N(b.y));
            }
            if (d.Length == 0) return "";
            return $"<path d=\"{d}\" stroke=\"{couleur}\" stroke-width=\"{N(epaisseur)}\" fill=\"none\" stroke-linecap=\"round\" opacity=\"{N(opacite)}\"/>\n";
        }

        static bool Separe(Carte carte, GrapheCellules g, int e, bool groupes)
        {
            int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
            if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) return false;
            int za = carte.ZoneDeCellule[a], zb = carte.ZoneDeCellule[b];
            if (za < 0 || zb < 0) return false;
            return groupes ? carte.GroupeDeZone[za] != carte.GroupeDeZone[zb] : za != zb;
        }

        static string Losange(float2 pos, float t)
        {
            float2 p = Projeter(pos);
            return $"<path d=\"M{N(p.x)} {N(p.y - t)}L{N(p.x + t)} {N(p.y)}L{N(p.x)} {N(p.y + t)}L{N(p.x - t)} {N(p.y)}Z\" " +
                   "fill=\"#f3ead6\" stroke=\"#2f2820\" stroke-width=\"2.5\"/>\n";
        }

        static string Pioche(float2 pos)
        {
            float2 p = Projeter(pos);
            return $"<g transform=\"translate({N(p.x - 9)},{N(p.y - 9)})\">" +
                   "<path d='M2 16 l9 -13 l4 2.6 l-9 13 z' fill='#2b241d' stroke='#f3ead6' stroke-width='1.6'/>" +
                   "<path d='M8 3 q8 -5 13 3' stroke='#2b241d' stroke-width='3.4' fill='none' stroke-linecap='round'/></g>\n";
        }

        static string Banniere(float2 pos, string numero)
        {
            float2 p = Projeter(pos);
            return $"<g transform=\"translate({N(p.x)},{N(p.y)})\">" +
                   "<rect x='-19' y='-22' width='38' height='34' rx='4' fill='#f3ead6' stroke='#2f2820' stroke-width='4.5'/>" +
                   "<path d='M-19 12 L0 24 L19 12 Z' fill='#f3ead6' stroke='#2f2820' stroke-width='4.5' stroke-linejoin='round'/>" +
                   $"<text x='0' y='6' fill='#2a2118' font-size='26' font-weight='700' text-anchor='middle'>{numero}</text></g>\n";
        }

        static float2 Centre(Carte carte, int zone)
        {
            float2 centre = float2.zero;
            float poids = 0f;
            foreach (int c in carte.CellulesDeZone[zone])
            {
                float a = carte.Graphe.Aire[c];
                centre += carte.Graphe.Sites[c] * a;
                poids += a;
            }
            return poids > 0f ? centre / poids : float2.zero;
        }

        // Le cadrage suit l'île : on ne garde que la frange de mer qui lui donne une rive.
        static float _ox, _oy, _echelle = 1f;

        static void CadrerSurIle(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (!carte.Terre[c]) continue;
                for (int i = g.DebutCoins[c]; i < g.DebutCoins[c + 1]; i++)
                {
                    float2 v = g.Coins[g.CoinsDeCellule[i]];
                    if (v.x < minX) minX = v.x;
                    if (v.y < minY) minY = v.y;
                    if (v.x > maxX) maxX = v.x;
                    if (v.y > maxY) maxY = v.y;
                }
            }
            if (minX > maxX) { _ox = 0f; _oy = 0f; _echelle = 1f; return; }

            float bord = math.max(maxX - minX, maxY - minY) * 0.06f;
            minX -= bord; minY -= bord; maxX += bord; maxY += bord;
            _echelle = Cote / math.max(maxX - minX, maxY - minY);
            // Le côté le plus court est centré dans le carré.
            _ox = minX - (Cote / _echelle - (maxX - minX)) * 0.5f;
            _oy = minY - (Cote / _echelle - (maxY - minY)) * 0.5f;
        }

        static float2 Projeter(float2 p)
        {
            return new float2(Marge + (p.x - _ox) * _echelle,
                              BandeauHaut + (Cote - (p.y - _oy) * _echelle));
        }

        static string N(float v) => v.ToString("0.#", Inv);
    }
}
