using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Mesure de bout en bout : quelle proportion de graines produit une carte complète, et
    /// quand ce n'est pas le cas, quelle phase a renoncé.
    ///
    /// Les campagnes des autres fichiers jugent chacune une phase sur son propre diagnostic.
    /// Celle-ci juge l'enchaînement, qui est ce que verra le joueur au moment de lancer une
    /// partie : une graine refusée devra être remplacée, et le lobby ne proposera que des
    /// graines validées.
    /// </summary>
    public class TestsPipeline
    {
        const int NbGraines = 40;

        [Test]
        public void Campagne_TauxDeReussiteDeBoutEnBout()
        {
            int reussies = 0;
            var echecsParPhase = new Dictionary<string, int>();
            long msTotal = 0, msMax = 0;

            for (int i = 0; i < NbGraines; i++)
            {
                ulong graine = 30000UL + (ulong)i;
                var carte = GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
                msTotal += rapport.MillisecondesTotal;
                if (rapport.MillisecondesTotal > msMax) msMax = rapport.MillisecondesTotal;

                if (carte != null) { reussies++; continue; }

                string phase = PhaseEnEchec(rapport);
                echecsParPhase.TryGetValue(phase, out int n);
                echecsParPhase[phase] = n + 1;
                TestContext.WriteLine($"graine {graine} : abandon en {phase} — {rapport.MotifEchec}");
            }

            double taux = (double)reussies / NbGraines;
            TestContext.WriteLine($"{reussies}/{NbGraines} graines complètes ({taux:P0}), " +
                                  $"{msTotal / NbGraines} ms en moyenne, {msMax} ms au pire");
            foreach (KeyValuePair<string, int> paire in echecsParPhase)
            {
                TestContext.WriteLine($"  abandons en {paire.Key} : {paire.Value}");
            }

            // Une graine sur dix refusée reste acceptable : la campagne de l'éditeur ne
            // retiendra que les graines validées, et le coût d'un abandon est de 200 ms.
            Assert.GreaterOrEqual(taux, 0.85,
                "Trop de graines sont abandonnées : le générateur n'est pas assez robuste.");
        }

        static string PhaseEnEchec(RapportGeneration rapport)
        {
            if (rapport.Ile == null || !rapport.Ile.Reussi) return "P3 (île)";
            if (rapport.Zones == null || !rapport.Zones.Reussi) return "P5 (zones)";
            if (rapport.Groupes == null || !rapport.Groupes.Reussi) return "P6 (groupes)";
            if (rapport.Passages == null || !rapport.Passages.Reussi) return "P7 (passages)";
            return "inconnue";
        }

        [Test]
        public void UneCarteCompleteEstCoherenteDeBoutEnBout()
        {
            var carte = GenerateurCarte.Generer(2026UL, new ParametresGeneration(), out RapportGeneration rapport);
            Assert.IsNotNull(carte, rapport.ToString());
            TestContext.WriteLine(rapport.ToString());

            // Chaque niveau de la hiérarchie couvre exactement le précédent.
            int cellulesDeTerre = 0;
            for (int c = 0; c < carte.NbCellules; c++)
            {
                if (carte.Terre[c]) cellulesDeTerre++;
            }

            int cellulesDansZones = 0;
            foreach (List<int> cellules in carte.CellulesDeZone) cellulesDansZones += cellules.Count;
            Assert.AreEqual(cellulesDeTerre, cellulesDansZones);

            int zonesDansGroupes = 0;
            foreach (List<int> zones in carte.ZonesDeGroupe) zonesDansGroupes += zones.Count;
            Assert.AreEqual(90, zonesDansGroupes);

            Assert.AreEqual(90, carte.NbZones);
            Assert.That(carte.NbGroupes, Is.InRange(23, 30));
            Assert.Greater(carte.Passages.Count, 0);
            Assert.IsTrue(rapport.Passages.GrapheConnexe);
        }
    }
}
