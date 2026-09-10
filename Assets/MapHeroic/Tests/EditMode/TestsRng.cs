using MapHeroic.Generation.Noyau;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J1 pour le générateur pseudo-aléatoire : même graine → mêmes 10 000 tirages,
    /// sous-flux stables, aucune allocation.
    /// </summary>
    public class TestsRng
    {
        const int NbTirages = 10000;

        static ulong EmpreinteDeSuite(Rng rng, int nbTirages)
        {
            HashFnv h = HashFnv.Nouveau();
            for (int i = 0; i < nbTirages; i++) h.Entier32(rng.Suivant());
            return h.Valeur;
        }

        [Test]
        public void MemeGraine_MemeSuiteDe10000Tirages()
        {
            ulong a = EmpreinteDeSuite(Rng.DepuisSeed(123456789UL), NbTirages);
            ulong b = EmpreinteDeSuite(Rng.DepuisSeed(123456789UL), NbTirages);
            Assert.AreEqual(a, b, "Deux flux issus de la même graine doivent produire la même suite.");
        }

        [Test]
        public void GrainesVoisines_SuitesDifferentes()
        {
            ulong a = EmpreinteDeSuite(Rng.DepuisSeed(1UL), NbTirages);
            ulong b = EmpreinteDeSuite(Rng.DepuisSeed(2UL), NbTirages);
            Assert.AreNotEqual(a, b, "Deux graines consécutives ne doivent pas donner la même suite.");
        }

        [Test]
        public void GraineZero_ProduitUnFluxNonDegenere()
        {
            // L'état entièrement nul est un point fixe de xoshiro : il doit être impossible.
            var rng = Rng.DepuisSeed(0UL);
            bool auMoinsUnNonNul = false;
            for (int i = 0; i < 64; i++)
            {
                if (rng.Suivant() != 0u) { auMoinsUnNonNul = true; break; }
            }
            Assert.IsTrue(auMoinsUnNonNul, "La graine 0 ne doit pas mener à un état dégénéré.");
        }

        [Test]
        public void Deriver_NeDependPasDeLaConsommationDuFluxParent()
        {
            var racineNeuve = Rng.DepuisSeed(42UL);

            var racineUsee = Rng.DepuisSeed(42UL);
            for (int i = 0; i < 977; i++) racineUsee.Suivant();

            ulong a = EmpreinteDeSuite(racineNeuve.Deriver(5), 512);
            ulong b = EmpreinteDeSuite(racineUsee.Deriver(5), 512);
            Assert.AreEqual(a, b,
                "Un sous-flux doit dériver de la graine d'origine, pas de l'état courant : " +
                "sinon l'ordre des phases changerait leurs tirages.");
        }

        [Test]
        public void Deriver_PhasesDifferentes_FluxDifferents()
        {
            var racine = Rng.DepuisSeed(42UL);
            ulong a = EmpreinteDeSuite(racine.Deriver(1), 512);
            ulong b = EmpreinteDeSuite(racine.Deriver(2), 512);
            ulong c = EmpreinteDeSuite(racine.Deriver(100), 512); // rejeu 0 de la phase 1
            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.AreNotEqual(b, c);
        }

        [Test]
        public void Deriver_ConservelaGraine()
        {
            var racine = Rng.DepuisSeed(7UL);
            Assert.AreEqual(7UL, racine.Graine);
        }

        [Test]
        public void Float01_ResteDansLIntervalleUnite()
        {
            var rng = Rng.DepuisSeed(99UL);
            for (int i = 0; i < NbTirages; i++)
            {
                float v = rng.Float01();
                Assert.IsFalse(float.IsNaN(v), "Float01 ne doit jamais produire de NaN.");
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }

        [Test]
        public void Float01_MoyenneProcheDeUnDemi()
        {
            var rng = Rng.DepuisSeed(2024UL);
            double somme = 0.0;
            for (int i = 0; i < NbTirages; i++) somme += rng.Float01();
            double moyenne = somme / NbTirages;
            Assert.That(moyenne, Is.EqualTo(0.5).Within(0.02), "Distribution visiblement biaisée.");
        }

        [Test]
        public void Entier_CouvreToutLIntervalleEtRienDAutre()
        {
            var rng = Rng.DepuisSeed(5UL);
            const int Min = -3;
            const int MaxExclu = 7;
            var vus = new bool[MaxExclu - Min];
            for (int i = 0; i < NbTirages; i++)
            {
                int v = rng.Entier(Min, MaxExclu);
                Assert.GreaterOrEqual(v, Min);
                Assert.Less(v, MaxExclu);
                vus[v - Min] = true;
            }
            for (int i = 0; i < vus.Length; i++)
            {
                Assert.IsTrue(vus[i], $"La valeur {i + Min} n'a jamais été tirée en {NbTirages} essais.");
            }
        }

        [Test]
        public void Entier_IntervalleVide_RendLaBorneBasse()
        {
            var rng = Rng.DepuisSeed(5UL);
            Assert.AreEqual(4, rng.Entier(4, 4));
        }

        [Test]
        public void Melanger_EstUnePermutationEtEstReproductible()
        {
            var a = new int[64];
            var b = new int[64];
            for (int i = 0; i < 64; i++) { a[i] = i; b[i] = i; }

            Rng.DepuisSeed(11UL).Melanger(a);
            Rng.DepuisSeed(11UL).Melanger(b);
            CollectionAssert.AreEqual(a, b, "Le mélange doit être reproductible à graine égale.");

            var trie = (int[])a.Clone();
            System.Array.Sort(trie);
            for (int i = 0; i < 64; i++) Assert.AreEqual(i, trie[i], "Le mélange doit être une permutation.");

            bool aBouge = false;
            for (int i = 0; i < 64; i++) { if (a[i] != i) { aBouge = true; break; } }
            Assert.IsTrue(aBouge, "Le mélange n'a rien déplacé.");
        }

        [Test]
        public void IndexPondere_RespecteLesPoidsEtIgnoreLesNuls()
        {
            var poids = new[] { 0f, 3f, 0f, 1f };
            var rng = Rng.DepuisSeed(777UL);
            var compte = new int[4];
            for (int i = 0; i < NbTirages; i++) compte[rng.IndexPondere(poids)]++;

            Assert.AreEqual(0, compte[0], "Un poids nul ne doit jamais être tiré.");
            Assert.AreEqual(0, compte[2], "Un poids nul ne doit jamais être tiré.");
            double rapport = (double)compte[1] / compte[3];
            Assert.That(rapport, Is.EqualTo(3.0).Within(0.25), "Le rapport des tirages doit suivre le rapport des poids.");
        }

        [Test]
        public void IndexPondere_TousPoidsNuls_RendMoinsUn()
        {
            var rng = Rng.DepuisSeed(1UL);
            Assert.AreEqual(-1, rng.IndexPondere(new[] { 0f, 0f }));
            Assert.AreEqual(-1, rng.IndexPondere(new float[0]));
            Assert.AreEqual(-1, rng.IndexPondere(null));
        }

        [Test]
        public void Suivant_NAllouePas()
        {
            var rng = Rng.DepuisSeed(3UL);
            uint puits = 0;
            // Un tour à blanc pour que la méthode soit compilée avant la mesure.
            for (int i = 0; i < 64; i++) puits += rng.Suivant();

            Assert.That(() =>
            {
                for (int i = 0; i < 1024; i++) puits += rng.Suivant();
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            Assert.AreNotEqual(uint.MaxValue, puits); // empêche l'élimination du calcul
        }
    }
}
