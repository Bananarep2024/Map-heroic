using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J5, première moitié : tous les groupes de 3 ou 4 zones, tous d'un seul
    /// tenant, graphe des groupes connexe, et frontières posées sur le relief.
    /// </summary>
    public class TestsGroupes
    {
        const int NbGrainesCampagne = 15;

        static Carte GenererValide(ulong graine)
        {
            var carte = GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
            Assert.IsNotNull(carte, $"Génération échouée pour la graine {graine} : {rapport}");
            return carte;
        }

        [Test]
        public void Campagne_TousLesGroupesValides()
        {
            int echecs = 0, rejeux = 0;
            int minGroupes = int.MaxValue, maxGroupes = 0;
            long msMax = 0;

            // La phase P6 est jugée sur son propre diagnostic : un rejet ultérieur par les
            // passages n'est pas un échec du regroupement.
            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                GenerateurCarte.Generer(9000UL + (ulong)i, new ParametresGeneration(),
                                        out RapportGeneration rapport);
                if (rapport.Groupes == null || !rapport.Groupes.Reussi)
                {
                    echecs++;
                    TestContext.WriteLine($"graine {9000 + i} : ÉCHEC — {rapport.Groupes?.MotifEchec ?? rapport.MotifEchec}");
                    continue;
                }

                rejeux += rapport.Groupes.Essais - 1;
                int nbGroupes = rapport.Groupes.NbGroupes;
                if (nbGroupes < minGroupes) minGroupes = nbGroupes;
                if (nbGroupes > maxGroupes) maxGroupes = nbGroupes;
                if (rapport.Groupes.Millisecondes > msMax) msMax = rapport.Groupes.Millisecondes;

                Assert.AreEqual(90, rapport.Groupes.NbGroupesDeTrois * 3 + rapport.Groupes.NbGroupesDeQuatre * 4,
                    $"Graine {9000 + i} : les tailles de groupes ne totalisent pas 90 zones.");
                Assert.AreEqual(nbGroupes, rapport.Groupes.NbGroupesDeTrois + rapport.Groupes.NbGroupesDeQuatre,
                    $"Graine {9000 + i} : un groupe n'a ni 3 ni 4 zones.");
            }

            TestContext.WriteLine($"{NbGrainesCampagne} graines : {echecs} échecs, {rejeux} rejeux, " +
                                  $"{minGroupes} à {maxGroupes} groupes, P6 au plus {msMax} ms");

            Assert.AreEqual(0, echecs, "Des graines n'ont produit aucun regroupement valide.");
            Assert.LessOrEqual(rejeux, NbGrainesCampagne / 5 + 1, "Trop de regroupements sont rejoués.");
        }

        [Test]
        public void NombreDeGroupesCoherentAvecQuatreVingtDixZones()
        {
            var carte = GenererValide(71UL);
            // 3a + 4b = 90 donne entre 23 (b maximal) et 30 groupes (a maximal).
            Assert.That(carte.NbGroupes, Is.InRange(23, 30));
            Assert.AreEqual(90, carte.NbGroupes * 4 - CompterZonesManquantes(carte),
                "Le compte des groupes de 3 et de 4 doit retomber sur 90 zones.");
        }

        static int CompterZonesManquantes(Carte carte)
        {
            int manquantes = 0;
            foreach (List<int> zones in carte.ZonesDeGroupe) manquantes += 4 - zones.Count;
            return manquantes;
        }

        [Test]
        public void ChaqueZoneAppartientAExactementUnGroupe()
        {
            var carte = GenererValide(72UL);
            var compte = new int[carte.NbZones];
            foreach (List<int> zones in carte.ZonesDeGroupe)
            {
                foreach (int z in zones) compte[z]++;
            }
            for (int z = 0; z < carte.NbZones; z++)
            {
                Assert.AreEqual(1, compte[z], $"La zone {z} appartient à {compte[z]} groupes.");
                Assert.That(carte.GroupeDeZone[z], Is.InRange(0, carte.NbGroupes - 1));
            }
        }

        [Test]
        public void ChaqueGroupeEstDUnSeulTenant()
        {
            var carte = GenererValide(73UL);
            for (int g = 0; g < carte.NbGroupes; g++)
            {
                List<int> zones = carte.ZonesDeGroupe[g];
                var vu = new HashSet<int> { zones[0] };
                var pile = new Stack<int>();
                pile.Push(zones[0]);

                while (pile.Count > 0)
                {
                    int z = pile.Pop();
                    foreach (int v in carte.ZonesVoisines[z])
                    {
                        if (carte.GroupeDeZone[v] != g || vu.Contains(v)) continue;
                        vu.Add(v);
                        pile.Push(v);
                    }
                }
                Assert.AreEqual(zones.Count, vu.Count, $"Le groupe {g} est en plusieurs morceaux.");
            }
        }

        [Test]
        public void LeGrapheDesGroupesEstConnexe()
        {
            var carte = GenererValide(74UL);
            var vu = new bool[carte.NbGroupes];
            var pile = new Stack<int>();
            pile.Push(0);
            vu[0] = true;
            int atteints = 1;

            while (pile.Count > 0)
            {
                int g = pile.Pop();
                foreach (int v in carte.GroupesVoisins[g])
                {
                    if (vu[v]) continue;
                    vu[v] = true;
                    atteints++;
                    pile.Push(v);
                }
            }
            Assert.AreEqual(carte.NbGroupes, atteints, "Le graphe des groupes doit être connexe.");
        }

        [Test]
        public void ChaqueGroupeAAuMoinsUnVoisin()
        {
            var carte = GenererValide(75UL);
            for (int g = 0; g < carte.NbGroupes; g++)
            {
                Assert.Greater(carte.GroupesVoisins[g].Count, 0, $"Le groupe {g} est isolé.");
            }
        }

        /// <summary>
        /// La bonne question n'est pas « les frontières de groupes sont-elles dures » — elles
        /// le sont forcément, puisque P5 a déjà posé toutes les frontières de zones sur le
        /// relief. C'est : parmi ces frontières déjà sélectionnées, le recuit retient-il les
        /// plus dures pour séparer les groupes ? On compare donc au sous-ensemble de
        /// référence, la moyenne de toutes les frontières de zones.
        /// </summary>
        [Test]
        public void LeRecuitRetientLesFrontieresLesPlusDures()
        {
            for (ulong graine = 76UL; graine < 80UL; graine++)
            {
                GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
                TestContext.WriteLine($"graine {graine} : {rapport.Groupes}");

                Assert.Greater(rapport.Groupes.DureteFrontieresGroupes,
                               rapport.Groupes.DureteFrontieresZones,
                    $"Les frontières choisies ne sont pas plus dures que la moyenne : {rapport.Groupes}");
                Assert.Greater(rapport.Groupes.DureteFrontieresGroupes,
                               rapport.Groupes.DureteFrontieresInternes,
                    $"Les frontières de groupes ne se distinguent pas de leur intérieur : {rapport.Groupes}");
            }
        }

        /// <summary>
        /// Le recuit ne peut jamais dégrader — il conserve la meilleure partition rencontrée —
        /// mais il peut ne rien améliorer, quand la construction gloutonne a déjà trouvé un
        /// optimum local. On vérifie donc les deux choses séparément : jamais de dégradation
        /// sur aucune graine, et une amélioration sur la majorité d'entre elles.
        /// </summary>
        [Test]
        public void LeRecuitNeDegradeJamaisEtAmelioreSouvent()
        {
            int ameliorees = 0, total = 0;

            for (ulong graine = 81UL; graine < 87UL; graine++)
            {
                GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
                DiagnosticGroupes g = rapport.Groupes;
                total++;

                Assert.LessOrEqual(g.EnergieFinale, g.EnergieInitiale,
                    $"Graine {graine} : le recuit a dégradé la partition — {g}");
                if (g.EnergieFinale < g.EnergieInitiale) ameliorees++;

                TestContext.WriteLine($"graine {graine} : {g.EnergieInitiale:F0} → {g.EnergieFinale:F0}");
            }

            Assert.Greater(ameliorees * 2, total,
                $"Le recuit n'améliore que {ameliorees} graines sur {total} : il ne sert à rien.");
        }

        [Test]
        public void MemeGraine_MemesGroupes()
        {
            var a = GenererValide(82UL);
            var b = GenererValide(82UL);
            CollectionAssert.AreEqual(a.GroupeDeZone, b.GroupeDeZone);
        }

        [Test]
        public void BudgetDeTempsRespecte()
        {
            GenerateurCarte.Generer(83UL, new ParametresGeneration(), out RapportGeneration rapport);
            TestContext.WriteLine(rapport.ToString());
            Assert.Less(rapport.Groupes.Millisecondes, 1500L, $"Regroupement trop lent : {rapport.Groupes}");
        }
    }
}
