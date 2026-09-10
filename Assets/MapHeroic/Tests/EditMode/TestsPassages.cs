using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J5, seconde moitié : 1 à 3 passages par groupe, graphe des groupes connexe,
    /// et tout passage assez large pour être franchi.
    /// </summary>
    public class TestsPassages
    {
        const int NbGrainesCampagne = 15;

        static Carte GenererValide(ulong graine)
        {
            var carte = GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
            Assert.IsNotNull(carte, $"Génération échouée pour la graine {graine} : {rapport}");
            return carte;
        }

        [Test]
        public void Campagne_PassagesConformes()
        {
            int echecs = 0, rejeux = 0;
            int degreMinGlobal = int.MaxValue, degreMaxGlobal = 0;
            float largeurMinGlobale = float.MaxValue;

            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                var carte = GenerateurCarte.Generer(9500UL + (ulong)i, new ParametresGeneration(),
                                                    out RapportGeneration rapport);
                if (carte == null)
                {
                    echecs++;
                    TestContext.WriteLine($"graine {9500 + i} : ÉCHEC — {rapport.MotifEchec}");
                    continue;
                }

                rejeux += rapport.Passages.Essais - 1;
                degreMinGlobal = System.Math.Min(degreMinGlobal, rapport.Passages.DegreMin);
                degreMaxGlobal = System.Math.Max(degreMaxGlobal, rapport.Passages.DegreMax);
                largeurMinGlobale = System.Math.Min(largeurMinGlobale, rapport.Passages.LargeurMin);

                Assert.IsTrue(rapport.Passages.GrapheConnexe,
                    $"Graine {9500 + i} : des groupes sont inaccessibles.");
            }

            TestContext.WriteLine($"{NbGrainesCampagne} graines : {echecs} échecs, {rejeux} rejeux, " +
                                  $"degrés {degreMinGlobal} à {degreMaxGlobal}, largeur minimale {largeurMinGlobale:F1} m");

            Assert.AreEqual(0, echecs, "Des graines n'ont produit aucun réseau de passages valide.");
            Assert.GreaterOrEqual(degreMinGlobal, 1, "Un groupe se retrouve sans aucun passage.");
            Assert.LessOrEqual(degreMaxGlobal, 3, "Un groupe dépasse trois passages.");
            Assert.GreaterOrEqual(largeurMinGlobale, 28f, "Un passage est trop étroit.");
        }

        [Test]
        public void ChaqueGroupeAEntreUnEtTroisPassages()
        {
            var carte = GenererValide(84UL);
            var degre = new int[carte.NbGroupes];
            foreach (Passage passage in carte.Passages)
            {
                degre[passage.GroupeA]++;
                degre[passage.GroupeB]++;
            }
            for (int g = 0; g < carte.NbGroupes; g++)
            {
                Assert.That(degre[g], Is.InRange(1, 3), $"Le groupe {g} a {degre[g]} passages.");
            }
        }

        [Test]
        public void LeReseauDePassagesRelieTousLesGroupes()
        {
            var carte = GenererValide(85UL);
            var voisins = new List<int>[carte.NbGroupes];
            for (int g = 0; g < carte.NbGroupes; g++) voisins[g] = new List<int>();
            foreach (Passage passage in carte.Passages)
            {
                voisins[passage.GroupeA].Add(passage.GroupeB);
                voisins[passage.GroupeB].Add(passage.GroupeA);
            }

            var vu = new bool[carte.NbGroupes];
            var pile = new Stack<int>();
            pile.Push(0);
            vu[0] = true;
            int atteints = 1;
            while (pile.Count > 0)
            {
                int g = pile.Pop();
                foreach (int v in voisins[g])
                {
                    if (vu[v]) continue;
                    vu[v] = true;
                    atteints++;
                    pile.Push(v);
                }
            }
            Assert.AreEqual(carte.NbGroupes, atteints,
                "En n'empruntant que les passages, tout groupe doit être atteignable.");
        }

        [Test]
        public void ChaquePassageEstAssezLargeEtBienForme()
        {
            var carte = GenererValide(86UL);
            var p = new ParametresPassages();

            foreach (Passage passage in carte.Passages)
            {
                Assert.GreaterOrEqual(passage.Largeur, p.LargeurMin, $"Passage {passage.Id} trop étroit.");
                Assert.That(passage.AretesFines.Length, Is.InRange(p.AretesMin, p.AretesMax));
                Assert.AreEqual(passage.AretesFines.Length + 1, passage.Coins.Length,
                    "Un passage a un coin de plus que d'arêtes.");
                Assert.AreNotEqual(passage.GroupeA, passage.GroupeB);
                Assert.Greater(passage.Cellules.Length, 0);
            }
        }

        [Test]
        public void UnSeulPassageParFrontiere()
        {
            var carte = GenererValide(87UL);
            var vues = new HashSet<long>();
            foreach (Passage passage in carte.Passages)
            {
                int a = System.Math.Min(passage.GroupeA, passage.GroupeB);
                int b = System.Math.Max(passage.GroupeA, passage.GroupeB);
                long cle = ((long)a << 32) | (uint)b;
                Assert.IsTrue(vues.Add(cle), $"Deux passages entre les groupes {a} et {b}.");
            }
        }

        [Test]
        public void LesCellulesDesPassagesSontReservees()
        {
            var carte = GenererValide(88UL);
            foreach (Passage passage in carte.Passages)
            {
                foreach (int c in passage.Cellules)
                {
                    Assert.IsTrue(carte.CelluleReservee[c],
                        $"La cellule {c} du passage {passage.Id} n'est pas réservée.");
                }
            }
            Assert.Greater(carte.Passages.Count, 0);
        }

        [Test]
        public void LesFrontieresSontDesChainesContinues()
        {
            var carte = GenererValide(90UL);
            GrapheCellules g = carte.Graphe;

            foreach (FrontiereGroupes f in carte.FrontieresGroupes)
            {
                Assert.AreEqual(f.Chaine.Length + 1, f.Coins.Length);
                for (int i = 0; i < f.Chaine.Length; i++)
                {
                    int e = f.Chaine[i];
                    int a = g.AreteCoinA[e], b = g.AreteCoinB[e];
                    bool enchaine = (a == f.Coins[i] && b == f.Coins[i + 1])
                                 || (b == f.Coins[i] && a == f.Coins[i + 1]);
                    Assert.IsTrue(enchaine,
                        $"La frontière {f.GroupeA}-{f.GroupeB} n'est pas continue à l'arête {i}.");
                }
            }
        }

        [Test]
        public void LesPassagesSuiventLeurFrontiere()
        {
            var carte = GenererValide(91UL);
            foreach (FrontiereGroupes f in carte.FrontieresGroupes)
            {
                if (f.Passage < 0) continue;
                Passage passage = carte.Passages[f.Passage];
                Assert.AreEqual(f.GroupeA, passage.GroupeA);
                Assert.AreEqual(f.GroupeB, passage.GroupeB);

                var deLaChaine = new HashSet<int>(f.Chaine);
                foreach (int e in passage.AretesFines)
                {
                    Assert.IsTrue(deLaChaine.Contains(e),
                        "Un passage doit emprunter les arêtes de sa propre frontière.");
                }
            }
        }

        [Test]
        public void MemeGraine_MemesPassages()
        {
            var a = GenererValide(92UL);
            var b = GenererValide(92UL);
            Assert.AreEqual(a.Passages.Count, b.Passages.Count);
            for (int i = 0; i < a.Passages.Count; i++)
            {
                Assert.AreEqual(a.Passages[i].GroupeA, b.Passages[i].GroupeA);
                Assert.AreEqual(a.Passages[i].GroupeB, b.Passages[i].GroupeB);
                CollectionAssert.AreEqual(a.Passages[i].AretesFines, b.Passages[i].AretesFines);
            }
        }

        [Test]
        public void BudgetDeTempsRespecte()
        {
            GenerateurCarte.Generer(93UL, new ParametresGeneration(), out RapportGeneration rapport);
            TestContext.WriteLine(rapport.ToString());
            Assert.Less(rapport.Passages.Millisecondes, 500L, $"Passages trop lents : {rapport.Passages}");
        }
    }
}
