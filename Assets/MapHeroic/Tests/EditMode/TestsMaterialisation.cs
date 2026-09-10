using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J6 : toute arête inter-groupes hors passage bloque, montagnes et lacs sont
    /// englobés et non constructibles, chaque zone garde plus de la moitié de sa surface
    /// constructible et reste praticable d'un seul tenant, chaque massif offre une mine.
    /// </summary>
    public class TestsMaterialisation
    {
        const int NbGrainesCampagne = 12;

        static Carte GenererValide(ulong graine)
        {
            var carte = GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
            Assert.IsNotNull(carte, $"Génération échouée pour la graine {graine} : {rapport}");
            return carte;
        }

        [Test]
        public void Campagne_GarantiesDeMaterialisation()
        {
            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                ulong graine = 11000UL + (ulong)i;
                GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
                Assert.IsNotNull(rapport.Materialisation, $"Graine {graine} n'a pas atteint la matérialisation.");
                var m = rapport.Materialisation;

                Assert.AreEqual(0, m.NbAretesNonBloquantes, $"Graine {graine} : {m}");
                Assert.AreEqual(0, m.NbZonesCoupees, $"Graine {graine} : {m}");
                Assert.AreEqual(0, m.NbGroupesCoupes, $"Graine {graine} : {m}");
                Assert.LessOrEqual(m.PartNonConstructibleMax, 0.45f, $"Graine {graine} : {m}");
                Assert.LessOrEqual(m.PenteMaxHorsMassif, 41f, $"Graine {graine} : {m}");
                Assert.Greater(m.NbSocketsMines, 0,
                    $"Graine {graine} : aucun gisement de minerai sur la carte — {m}");
            }
        }

        [Test]
        public void ToutesLesAretesInterGroupesBloquentSaufLesPassages()
        {
            var carte = GenererValide(120UL);
            GrapheCellules g = carte.Graphe;

            var aretesDePassage = new HashSet<int>();
            foreach (Passage passage in carte.Passages)
            {
                foreach (int e in passage.AretesFines) aretesDePassage.Add(e);
            }

            int verifiees = 0;
            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;

                int ga = carte.GroupeDeZone[carte.ZoneDeCellule[a]];
                int gb = carte.GroupeDeZone[carte.ZoneDeCellule[b]];
                if (ga == gb) continue;

                if (aretesDePassage.Contains(e))
                {
                    Assert.AreEqual(TypeFrontiere.Col, carte.TypeArete[e], $"L'arête {e} est un passage.");
                    continue;
                }

                Assert.IsTrue(carte.AreteBloquante[e],
                    $"L'arête {e} sépare les groupes {ga} et {gb} sans rien bloquer.");
                verifiees++;
            }
            Assert.Greater(verifiees, 100, "Trop peu d'arêtes inter-groupes pour conclure.");
        }

        [Test]
        public void MontagnesEtLacsSontNonConstructiblesEtDansLeursZones()
        {
            var carte = GenererValide(121UL);
            for (int c = 0; c < carte.NbCellules; c++)
            {
                bool massif = carte.ADrapeau(c, DrapeauxCellule.Massif);
                bool lac = carte.ADrapeau(c, DrapeauxCellule.Lac);
                if (!massif && !lac) continue;

                Assert.IsTrue(carte.ADrapeau(c, DrapeauxCellule.NonConstructible),
                    $"La cellule {c} porte un relief sans être marquée non constructible.");
                Assert.IsTrue(carte.Terre[c], $"La cellule {c} est en mer.");
                Assert.GreaterOrEqual(carte.ZoneDeCellule[c], 0,
                    $"La cellule {c} n'appartient à aucune zone : le relief doit être englobé.");
            }
        }

        [Test]
        public void AucunObstacleSurUneCelluleDePassage()
        {
            var carte = GenererValide(122UL);
            foreach (Passage passage in carte.Passages)
            {
                foreach (int c in passage.Cellules)
                {
                    Assert.IsFalse(carte.ADrapeau(c, DrapeauxCellule.Massif),
                        $"Un massif barre le passage {passage.Id}.");
                    Assert.IsFalse(carte.ADrapeau(c, DrapeauxCellule.Lac),
                        $"Un lac barre le passage {passage.Id}.");
                }
            }
        }

        [Test]
        public void ChaqueZoneGardeLaMajoriteDeSaSurfaceConstructible()
        {
            var carte = GenererValide(123UL);
            for (int z = 0; z < carte.NbZones; z++)
            {
                int nonConstructibles = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (carte.ADrapeau(c, DrapeauxCellule.NonConstructible)) nonConstructibles++;
                }
                float part = (float)nonConstructibles / carte.CellulesDeZone[z].Count;
                Assert.LessOrEqual(part, 0.45f, $"La zone {z} est non constructible à {part:P0}.");
            }
        }

        [Test]
        public void ChaqueZoneEtChaqueGroupeRestentPraticablesDUnSeulTenant()
        {
            var carte = GenererValide(124UL);
            GrapheCellules g = carte.Graphe;

            bool Praticable(int c) => carte.Terre[c] && !carte.ADrapeau(c, DrapeauxCellule.NonConstructible);

            for (int z = 0; z < carte.NbZones; z++)
            {
                int zone = z;
                VerifierUneComposante(g, carte.CellulesDeZone[z], Praticable,
                                      c => carte.ZoneDeCellule[c] == zone, $"la zone {z}");
            }

            for (int gr = 0; gr < carte.NbGroupes; gr++)
            {
                var cellules = new List<int>();
                foreach (int z in carte.ZonesDeGroupe[gr]) cellules.AddRange(carte.CellulesDeZone[z]);
                int groupe = gr;
                VerifierUneComposante(g, cellules, Praticable,
                    c => carte.ZoneDeCellule[c] >= 0 && carte.GroupeDeZone[carte.ZoneDeCellule[c]] == groupe,
                    $"le groupe {gr}");
            }
        }

        static void VerifierUneComposante(GrapheCellules g, List<int> cellules,
                                          System.Func<int, bool> praticable,
                                          System.Func<int, bool> dedans, string quoi)
        {
            int depart = -1, total = 0;
            foreach (int c in cellules)
            {
                if (!praticable(c)) continue;
                total++;
                if (depart < 0) depart = c;
            }
            Assert.Greater(total, 0, $"{quoi} n'a aucune cellule praticable.");

            var vu = new HashSet<int> { depart };
            var pile = new Stack<int>();
            pile.Push(depart);
            int atteintes = 0;
            while (pile.Count > 0)
            {
                int c = pile.Pop();
                atteintes++;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || vu.Contains(v) || !dedans(v) || !praticable(v)) continue;
                    vu.Add(v);
                    pile.Push(v);
                }
            }
            Assert.AreEqual(total, atteintes, $"{quoi} est praticable en plusieurs morceaux.");
        }

        [Test]
        public void ChaqueZoneMontagneOffreAuMoinsUneMine()
        {
            var carte = GenererValide(125UL);
            int zonesMontagne = 0;

            for (int z = 0; z < carte.NbZones; z++)
            {
                if (!carte.ZoneMontagne[z]) continue;
                zonesMontagne++;

                int mines = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (carte.ADrapeau(c, DrapeauxCellule.SocketMine)) mines++;
                }
                Assert.Greater(mines, 0,
                    $"La zone Montagne {z} n'a aucun emplacement de mine : elle produirait du minerai sans pouvoir l'extraire.");
            }
            Assert.Greater(zonesMontagne, 0, "Aucune zone Montagne sur la carte.");
        }

        [Test]
        public void LesSocketsSontSurDesCellulesConstructibles()
        {
            var carte = GenererValide(126UL);
            for (int c = 0; c < carte.NbCellules; c++)
            {
                bool socket = carte.ADrapeau(c, DrapeauxCellule.SocketMine)
                           || carte.ADrapeau(c, DrapeauxCellule.SocketPeche);
                if (!socket) continue;
                Assert.IsFalse(carte.ADrapeau(c, DrapeauxCellule.NonConstructible),
                    $"Le socket de la cellule {c} est sur du terrain non constructible.");
            }
        }

        [Test]
        public void LePoissonNestPasUniversel()
        {
            // Une ressource que toutes les zones possèdent ne différencie rien : les rivières
            // prolongées, qui séparent la plupart des groupes, n'en donnent donc pas.
            var carte = GenererValide(127UL);
            int riveraines = 0;
            foreach (bool r in carte.ZoneRiveraine)
            {
                if (r) riveraines++;
            }
            Assert.That(riveraines, Is.InRange(10, 75),
                $"{riveraines} zones riveraines sur 90 : la pêche perd son intérêt.");
        }

        [Test]
        public void LesLacsRestentCoherents()
        {
            var carte = GenererValide(128UL);
            foreach (LacData lac in carte.Lacs)
            {
                Assert.Greater(lac.Cellules.Length, 0, "Un lac vide ne doit pas subsister.");
                foreach (int c in lac.Cellules)
                {
                    Assert.IsTrue(carte.ADrapeau(c, DrapeauxCellule.Lac),
                        $"La cellule {c} est listée dans un lac sans en porter le drapeau.");
                }
            }
        }

        [Test]
        public void MemeGraine_MemeMaterialisation()
        {
            var a = GenererValide(129UL);
            var b = GenererValide(129UL);
            CollectionAssert.AreEqual(a.Drapeaux, b.Drapeaux);
            CollectionAssert.AreEqual(a.AreteBloquante, b.AreteBloquante);
            for (int i = 0; i < a.NbCoins; i++)
            {
                Assert.AreEqual(a.HauteurCoin[i], b.HauteurCoin[i], $"Altitude différente au coin {i}.");
            }
        }

        [Test]
        public void LesAltitudesSontQuantifiees()
        {
            var carte = GenererValide(130UL);
            foreach (float h in carte.HauteurCoin)
            {
                float attendu = Mathf(h);
                Assert.AreEqual(attendu, h, 1e-4f, "Les altitudes doivent être au pas de 5 cm.");
            }
        }

        static float Mathf(float h) => (float)System.Math.Floor(h * 20.0 + 0.5) / 20f;
    }
}
