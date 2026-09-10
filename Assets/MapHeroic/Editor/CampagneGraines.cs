using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MapHeroic.Generation;
using MapHeroic.Generation.Terrain;
using UnityEditor;
using UnityEngine;

namespace MapHeroic.EditeurCarte
{
    public sealed class ResultatCampagne
    {
        public int NbGraines;
        public int NbReussies;
        public Dictionary<string, int> EchecsParPhase = new Dictionary<string, int>();
        public Dictionary<string, int> ReglesEnfreintes = new Dictionary<string, int>();
        public long MillisecondesMoyennes;
        public long MillisecondesMax;
        public string CheminRapport;

        /// <summary>Graines dont la carte est complète et conforme : celles que le lobby proposera.</summary>
        public List<ulong> GrainesValidees = new List<ulong>();

        public float TauxReussite => NbGraines > 0 ? (float)NbReussies / NbGraines : 0f;

        public override string ToString()
        {
            var texte = new StringBuilder();
            texte.AppendLine($"{NbReussies}/{NbGraines} graines conformes ({TauxReussite:P1})");
            texte.AppendLine($"{MillisecondesMoyennes} ms en moyenne, {MillisecondesMax} ms au pire");
            foreach (KeyValuePair<string, int> paire in EchecsParPhase)
            {
                texte.AppendLine($"  abandons en {paire.Key} : {paire.Value}");
            }
            foreach (KeyValuePair<string, int> paire in ReglesEnfreintes)
            {
                texte.AppendLine($"  règle {paire.Key} enfreinte : {paire.Value} fois");
            }
            if (!string.IsNullOrEmpty(CheminRapport)) texte.AppendLine($"Rapport : {CheminRapport}");
            return texte.ToString();
        }
    }

    /// <summary>
    /// Exécution en lot de N graines, avec rapport CSV.
    ///
    /// Cette campagne est le seul moyen honnête de qualifier le générateur : une carte
    /// examinée à l'œil ne dit rien du millier d'autres qu'un joueur rencontrera. Elle sert
    /// aussi à constituer la liste des graines validées, la seule que le lobby proposera —
    /// une graine refusée en cours de partie ne serait pas rattrapable.
    /// </summary>
    public static class CampagneGraines
    {
        /// <summary>
        /// Point d'entrée en ligne de commande, pour lancer la campagne sans ouvrir l'éditeur :
        /// <c>Unity -batchmode -executeMethod MapHeroic.EditeurCarte.CampagneGraines.DepuisLigneDeCommande</c>.
        /// Les arguments <c>-graine</c> et <c>-nbGraines</c> sont facultatifs.
        /// </summary>
        public static void DepuisLigneDeCommande()
        {
            ulong premiere = 1UL;
            int nombre = 200;

            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-graine") ulong.TryParse(arguments[i + 1], out premiere);
                else if (arguments[i] == "-nbGraines") int.TryParse(arguments[i + 1], out nombre);
            }

            ResultatCampagne resultat = Executer(premiere, nombre, new ParametresGeneration(), true);
            Debug.Log($"[CAMPAGNE]\n{resultat}");

            // Le code de sortie sert d'assertion en intégration continue.
            EditorApplication.Exit(resultat.TauxReussite >= 0.95f ? 0 : 2);
        }

        public static ResultatCampagne Executer(ulong premiereGraine, int nbGraines,
                                                ParametresGeneration parametres, bool ecrireRapport,
                                                Action<float, string> progression = null)
        {
            var resultat = new ResultatCampagne { NbGraines = nbGraines };
            var lignes = new StringBuilder();
            lignes.AppendLine("graine;reussie;phase_echec;ms;cellules;terre;zones;groupes;passages;" +
                              "ecart_aire;ecart_departs;massifs;lacs;non_constructible_max;pente_max;" +
                              "zones_riveraines;ponts;empreinte;regles_enfreintes");

            long total = 0;

            for (int i = 0; i < nbGraines; i++)
            {
                ulong graine = premiereGraine + (ulong)i;
                if (progression != null && (i % 10 == 0 || i == nbGraines - 1))
                {
                    progression((float)i / nbGraines, $"Graine {graine} ({i + 1}/{nbGraines})");
                }

                var carte = GenerateurCarte.Generer(graine, parametres, out RapportGeneration rapport);
                total += rapport.MillisecondesTotal;
                resultat.MillisecondesMax = Math.Max(resultat.MillisecondesMax, rapport.MillisecondesTotal);

                if (carte == null)
                {
                    string phase = PhaseEnEchec(rapport);
                    Incrementer(resultat.EchecsParPhase, phase);
                    lignes.AppendLine(Csv(graine, false, phase, rapport, null, null, ""));
                    continue;
                }

                List<ResultatRegle> regles = Validation.Verifier(carte, parametres);
                var enfreintes = new List<string>();
                foreach (ResultatRegle regle in regles)
                {
                    if (regle.Conforme) continue;
                    enfreintes.Add(regle.Code);
                    Incrementer(resultat.ReglesEnfreintes, regle.Code);
                }

                MapData data = MapData.Depuis(carte, parametres.Maillage.TailleCarte);
                bool conforme = enfreintes.Count == 0;
                if (conforme)
                {
                    resultat.NbReussies++;
                    resultat.GrainesValidees.Add(graine);
                }
                lignes.AppendLine(Csv(graine, conforme, "", rapport, carte, data, string.Join("|", enfreintes)));
            }

            resultat.MillisecondesMoyennes = nbGraines > 0 ? total / nbGraines : 0;

            if (ecrireRapport)
            {
                string dossier = Path.Combine(Application.dataPath, "MapHeroic", "Rapports");
                Directory.CreateDirectory(dossier);
                string nom = $"campagne_{premiereGraine}_{nbGraines}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                resultat.CheminRapport = Path.Combine(dossier, nom);
                File.WriteAllText(resultat.CheminRapport, lignes.ToString(), new UTF8Encoding(true));
                AssetDatabase.Refresh();
            }

            return resultat;
        }

        static string PhaseEnEchec(RapportGeneration rapport)
        {
            if (rapport.Ile == null || !rapport.Ile.Reussi) return "P3 île";
            if (rapport.Zones == null || !rapport.Zones.Reussi) return "P5 zones";
            if (rapport.Groupes == null || !rapport.Groupes.Reussi) return "P6 groupes";
            if (rapport.Passages == null || !rapport.Passages.Reussi) return "P7 passages";
            if (rapport.Materialisation == null || !rapport.Materialisation.Reussi) return "P8 matérialisation";
            if (rapport.Departs == null || !rapport.Departs.Reussi) return "P9 départs";
            if (rapport.Terrains == null || !rapport.Terrains.Reussi) return "P10 terrains";
            return "inconnue";
        }

        static void Incrementer(Dictionary<string, int> table, string cle)
        {
            table.TryGetValue(cle, out int n);
            table[cle] = n + 1;
        }

        static string Csv(ulong graine, bool reussie, string phase, RapportGeneration rapport,
                          Carte carte, MapData data, string enfreintes)
        {
            var c = CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("F3", c);

            return string.Join(";",
                graine.ToString(c),
                reussie ? "1" : "0",
                phase,
                rapport.MillisecondesTotal.ToString(c),
                (rapport.Maillage?.NbPointsInterieurs ?? 0).ToString(c),
                (rapport.Ile?.NbCellulesTerre ?? 0).ToString(c),
                (carte?.NbZones ?? 0).ToString(c),
                (carte?.NbGroupes ?? 0).ToString(c),
                (rapport.Passages?.NbPassages ?? 0).ToString(c),
                F(rapport.Zones?.EcartMax ?? 0f),
                F(rapport.Departs?.EcartScore ?? 0f),
                (rapport.Materialisation?.NbMassifs ?? 0).ToString(c),
                (rapport.Materialisation?.NbLacs ?? 0).ToString(c),
                F(rapport.Materialisation?.PartNonConstructibleMax ?? 0f),
                F(rapport.Materialisation?.PenteMaxHorsMassif ?? 0f),
                (rapport.Materialisation?.NbZonesRiveraines ?? 0).ToString(c),
                (rapport.Terrains?.NbPonts ?? 0).ToString(c),
                data != null ? $"0x{data.Empreinte:X16}" : "",
                enfreintes);
        }
    }
}
