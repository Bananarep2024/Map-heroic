using MapHeroic.Generation.Noyau;
using NUnit.Framework;
using Unity.Mathematics;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Le bruit doit être borné, continu, reproductible et sensible à la graine — c'est sur
    /// lui que reposent la forme de l'île, le relief et les crêtes.
    /// </summary>
    public class TestsBruit
    {
        [Test]
        public void Valeur_ResteBorneeEtFinie()
        {
            for (int y = -200; y <= 200; y++)
            {
                for (int x = -200; x <= 200; x += 7)
                {
                    float v = Bruit.Valeur(new float2(x * 0.37f, y * 0.41f), 1234u);
                    Assert.IsFalse(float.IsNaN(v), "Le bruit a produit NaN.");
                    Assert.GreaterOrEqual(v, -1f);
                    Assert.LessOrEqual(v, 1f);
                }
            }
        }

        [Test]
        public void Valeur_EstReproductible()
        {
            var p = new float2(12.34f, -56.78f);
            Assert.AreEqual(Bruit.Valeur(p, 42u), Bruit.Valeur(p, 42u));
        }

        [Test]
        public void Valeur_DependDeLaGraine()
        {
            var p = new float2(3.5f, 7.25f);
            Assert.AreNotEqual(Bruit.Valeur(p, 1u), Bruit.Valeur(p, 2u));
        }

        [Test]
        public void Valeur_EstContinue()
        {
            // Un déplacement d'un centième de maille ne doit pas faire sauter la valeur.
            const float Pas = 0.01f;
            float precedent = Bruit.Valeur(new float2(0f, 0.5f), 7u);
            for (int i = 1; i <= 2000; i++)
            {
                float v = Bruit.Valeur(new float2(i * Pas, 0.5f), 7u);
                Assert.Less(math.abs(v - precedent), 0.15f,
                    $"Discontinuité du bruit en x = {i * Pas}.");
                precedent = v;
            }
        }

        [Test]
        public void Valeur_EstLisseAuxNoeudsDuTreillis()
        {
            // L'interpolation quintique annule la dérivée aux nœuds : la valeur juste avant
            // et juste après un nœud entier doit être quasi identique.
            for (int n = -5; n <= 5; n++)
            {
                float avant = Bruit.Valeur(new float2(n - 0.001f, 2.5f), 99u);
                float apres = Bruit.Valeur(new float2(n + 0.001f, 2.5f), 99u);
                Assert.Less(math.abs(apres - avant), 0.01f, $"Cassure au nœud {n}.");
            }
        }

        [Test]
        public void Fbm_ResteBorneEtFini()
        {
            for (int i = 0; i < 3000; i++)
            {
                var p = new float2(i * 0.63f, i * -0.29f);
                float v = Bruit.Fbm(p, 4, 0.003f, 555u);
                Assert.IsFalse(float.IsNaN(v));
                Assert.GreaterOrEqual(v, -1f);
                Assert.LessOrEqual(v, 1f);
            }
        }

        [Test]
        public void Fbm_NombreDOctavesAberrant_RestePropre()
        {
            var p = new float2(1f, 1f);
            Assert.IsFalse(float.IsNaN(Bruit.Fbm(p, 0, 0.01f, 1u)));
            Assert.IsFalse(float.IsNaN(Bruit.Fbm(p, -5, 0.01f, 1u)));
            Assert.IsFalse(float.IsNaN(Bruit.Fbm(p, 1000, 0.01f, 1u)));
        }

        [Test]
        public void Crete_ResteDansZeroUn()
        {
            for (int i = 0; i < 3000; i++)
            {
                var p = new float2(i * 0.17f, i * 0.83f);
                float v = Bruit.Crete(p, 3, 0.006f, 31u);
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, 1f);
            }
        }

        [Test]
        public void CoordonneesAberrantes_NeProduisentPasDeNaN()
        {
            float[] pieges = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, -float.MaxValue, 1e30f };
            foreach (float a in pieges)
            {
                foreach (float b in pieges)
                {
                    Assert.IsFalse(float.IsNaN(Bruit.Valeur(new float2(a, b), 1u)), $"Valeur({a}, {b}) a produit NaN.");
                    Assert.IsFalse(float.IsNaN(Bruit.Fbm(new float2(a, b), 4, 0.01f, 1u)), $"Fbm({a}, {b}) a produit NaN.");
                }
            }
        }

        [Test]
        public void Hash_AvalancheCorrecte()
        {
            // Deux nœuds voisins doivent donner des valeurs sans corrélation visible.
            int identiques = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (Bruit.Hash(i, 0, 1u) == Bruit.Hash(i + 1, 0, 1u)) identiques++;
            }
            Assert.AreEqual(0, identiques, "Collisions entre nœuds voisins du treillis.");
        }
    }
}
