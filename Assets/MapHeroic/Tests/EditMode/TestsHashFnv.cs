using MapHeroic.Generation.Noyau;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// L'empreinte sert à comparer deux cartes générées sur deux machines : elle doit être
    /// conforme à FNV-1a 64 (constantes validées par les vecteurs de référence publiés) et
    /// sensible à l'ordre comme à la structure.
    /// </summary>
    public class TestsHashFnv
    {
        [Test]
        public void ConstantesConformesAuxVecteursDeReference()
        {
            Assert.AreEqual(0xcbf29ce484222325UL, HashFnv.Nouveau().Valeur, "Valeur de base FNV-1a 64 incorrecte.");

            var h = HashFnv.Nouveau();
            h.Octet((byte)'a');
            Assert.AreEqual(0xaf63dc4c8601ec8cUL, h.Valeur, "FNV-1a 64 de \"a\" incorrect : le nombre premier est faux.");

            var h2 = HashFnv.Nouveau();
            foreach (char c in "foobar") h2.Octet((byte)c);
            Assert.AreEqual(0x85944171f73967e8UL, h2.Valeur, "FNV-1a 64 de \"foobar\" incorrect.");
        }

        [Test]
        public void MemeContenu_MemeEmpreinte()
        {
            var a = new[] { 1, 2, 3, -4 };
            var b = new[] { 1, 2, 3, -4 };
            Assert.AreEqual(HashFnv.De(a), HashFnv.De(b));
        }

        [Test]
        public void LOrdreCompte()
        {
            Assert.AreNotEqual(HashFnv.De(new[] { 1, 2 }), HashFnv.De(new[] { 2, 1 }));
        }

        [Test]
        public void TableauNul_SeDistingueDuTableauVide()
        {
            Assert.AreNotEqual(HashFnv.De(null), HashFnv.De(new int[0]));
        }

        [Test]
        public void DeuxChampsConcatenes_NeSeConfondentPas()
        {
            // Sans le hachage des longueurs, ({1}, {2, 3}) et ({1, 2}, {3}) donneraient la même suite.
            var x = HashFnv.Nouveau();
            x.Tableau(new[] { 1 });
            x.Tableau(new[] { 2, 3 });

            var y = HashFnv.Nouveau();
            y.Tableau(new[] { 1, 2 });
            y.Tableau(new[] { 3 });

            Assert.AreNotEqual(x.Valeur, y.Valeur, "Deux découpages différents produisent la même empreinte.");
        }

        [Test]
        public void TypesEntiersDistincts_SontPrisEnCompte()
        {
            var court = HashFnv.Nouveau();
            court.Tableau(new short[] { 1, 2, 3 });

            var entier = HashFnv.Nouveau();
            entier.Tableau(new[] { 1, 2, 3 });

            Assert.AreNotEqual(court.Valeur, entier.Valeur);
        }

        [Test]
        public void UnBitDeDifference_ChangeLEmpreinte()
        {
            var a = new int[1000];
            var b = new int[1000];
            b[737] = 1;
            Assert.AreNotEqual(HashFnv.De(a), HashFnv.De(b));
        }
    }
}
