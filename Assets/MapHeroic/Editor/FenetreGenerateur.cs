using System;
using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Terrain;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapHeroic.EditeurCarte
{
    /// <summary>
    /// Fenêtre « Map Heroic / Générateur de carte ».
    ///
    /// L'outil est fait pour itérer sur les RÈGLES avant d'investir dans la 3D : on tire une
    /// graine, on regarde le découpage, on lit les mesures. Le panneau de validation affiche
    /// pour chaque règle la valeur relevée, et pas seulement un verdict — un « ✓ » sans
    /// chiffre ne dit pas de combien on est passé près de la faute.
    /// </summary>
    public sealed class FenetreGenerateur : EditorWindow
    {
        readonly ParametresGeneration _parametres = new ParametresGeneration();

        ulong _graine = 20260910UL;
        Carte _carte;
        RapportGeneration _rapport;
        List<ResultatRegle> _regles = new List<ResultatRegle>();

        ApercuCarte _apercu;
        Label _resume;
        Label _survol;
        ScrollView _panneauRegles;
        ScrollView _panneauMesures;
        int _nbGrainesCampagne = 200;

        [MenuItem("Map Heroic/Générateur de carte")]
        public static void Ouvrir()
        {
            var fenetre = GetWindow<FenetreGenerateur>();
            fenetre.titleContent = new GUIContent("Générateur de carte");
            fenetre.minSize = new Vector2(980, 620);
        }

        public void CreateGUI()
        {
            VisualElement racine = rootVisualElement;
            racine.style.flexDirection = FlexDirection.Column;

            racine.Add(ConstruireBarre());

            var corps = new VisualElement();
            corps.style.flexDirection = FlexDirection.Row;
            corps.style.flexGrow = 1;
            racine.Add(corps);

            var colonneGauche = new VisualElement();
            colonneGauche.style.flexGrow = 1;
            colonneGauche.style.flexDirection = FlexDirection.Column;
            corps.Add(colonneGauche);

            _apercu = new ApercuCarte();
            _apercu.CelluleSurvolee += AfficherSurvol;
            colonneGauche.Add(_apercu);

            _survol = new Label("Survolez une cellule pour l'inspecter.");
            _survol.style.paddingLeft = 6;
            _survol.style.paddingTop = 4;
            _survol.style.paddingBottom = 4;
            _survol.style.whiteSpace = WhiteSpace.Normal;
            colonneGauche.Add(_survol);

            colonneGauche.Add(ConstruireCalques());

            var colonneDroite = new VisualElement();
            colonneDroite.style.width = 380;
            colonneDroite.style.borderLeftWidth = 1;
            colonneDroite.style.borderLeftColor = new Color(0f, 0f, 0f, 0.3f);
            corps.Add(colonneDroite);

            _resume = new Label("Aucune carte générée.");
            _resume.style.whiteSpace = WhiteSpace.Normal;
            _resume.style.paddingLeft = 8;
            _resume.style.paddingTop = 8;
            _resume.style.paddingRight = 8;
            colonneDroite.Add(_resume);

            colonneDroite.Add(Titre("Règles"));
            _panneauRegles = new ScrollView { style = { flexGrow = 1 } };
            colonneDroite.Add(_panneauRegles);

            colonneDroite.Add(Titre("Mesures par phase"));
            _panneauMesures = new ScrollView { style = { flexGrow = 1 } };
            colonneDroite.Add(_panneauMesures);

            Generer();
        }

        // -------------------------------------------------------------- interface

        VisualElement ConstruireBarre()
        {
            var barre = new VisualElement();
            barre.style.flexDirection = FlexDirection.Row;
            barre.style.flexWrap = Wrap.Wrap;
            barre.style.paddingLeft = 6;
            barre.style.paddingTop = 6;
            barre.style.paddingBottom = 6;
            barre.style.borderBottomWidth = 1;
            barre.style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);

            var champGraine = new TextField("Graine") { value = _graine.ToString() };
            champGraine.style.width = 220;
            champGraine.RegisterValueChangedCallback(evenement =>
            {
                if (ulong.TryParse(evenement.newValue, out ulong valeur)) _graine = valeur;
            });
            barre.Add(champGraine);

            barre.Add(Bouton("Aléatoire", () =>
            {
                _graine = (ulong)UnityEngine.Random.Range(1, int.MaxValue);
                champGraine.SetValueWithoutNotify(_graine.ToString());
                Generer();
            }));
            barre.Add(Bouton("Générer", Generer));
            barre.Add(Bouton("Graine suivante", () =>
            {
                _graine++;
                champGraine.SetValueWithoutNotify(_graine.ToString());
                Generer();
            }));

            var modeRemplissage = new EnumField("Remplissage", ModeRemplissage.Terrain);
            modeRemplissage.style.width = 240;
            modeRemplissage.RegisterValueChangedCallback(evenement =>
            {
                _apercu.Mode = (ModeRemplissage)evenement.newValue;
                _apercu.MarkDirtyRepaint();
            });
            barre.Add(modeRemplissage);

            var phase = new EnumField("Arrêter après", PhaseGeneration.Complet);
            phase.style.width = 260;
            phase.RegisterValueChangedCallback(evenement =>
            {
                _parametres.PhaseFinale = (PhaseGeneration)evenement.newValue;
                Generer();
            });
            barre.Add(phase);

            var champCampagne = new IntegerField("Graines") { value = _nbGrainesCampagne };
            champCampagne.style.width = 140;
            champCampagne.RegisterValueChangedCallback(evenement => _nbGrainesCampagne = Mathf.Max(1, evenement.newValue));
            barre.Add(champCampagne);
            barre.Add(Bouton("Tester N graines", LancerCampagne));

            return barre;
        }

        VisualElement ConstruireCalques()
        {
            var conteneur = new VisualElement();
            conteneur.style.flexDirection = FlexDirection.Row;
            conteneur.style.flexWrap = Wrap.Wrap;
            conteneur.style.paddingLeft = 6;
            conteneur.style.paddingBottom = 6;

            foreach (Calques calque in Enum.GetValues(typeof(Calques)))
            {
                if (calque == Calques.Aucun) continue;
                Calques valeur = calque;
                var bascule = new Toggle(NomCalque(valeur)) { value = (_apercu.CalquesActifs & valeur) != 0 };
                bascule.style.marginRight = 10;
                bascule.RegisterValueChangedCallback(evenement =>
                {
                    if (evenement.newValue) _apercu.CalquesActifs |= valeur;
                    else _apercu.CalquesActifs &= ~valeur;
                    _apercu.MarkDirtyRepaint();
                });
                conteneur.Add(bascule);
            }
            return conteneur;
        }

        static string NomCalque(Calques calque)
        {
            switch (calque)
            {
                case Calques.ContoursZones: return "Zones";
                case Calques.ContoursGroupes: return "Groupes";
                case Calques.Reliefs: return "Reliefs";
                case Calques.Eau: return "Eau";
                case Calques.Passages: return "Passages";
                case Calques.Ponts: return "Ponts";
                case Calques.Departs: return "Départs";
                case Calques.Sockets: return "Sockets";
                case Calques.CellulesFines: return "Cellules";
                default: return calque.ToString();
            }
        }

        static Button Bouton(string texte, Action action)
        {
            var bouton = new Button(action) { text = texte };
            bouton.style.marginLeft = 6;
            bouton.style.height = 20;
            return bouton;
        }

        static Label Titre(string texte)
        {
            var titre = new Label(texte);
            titre.style.unityFontStyleAndWeight = FontStyle.Bold;
            titre.style.paddingLeft = 8;
            titre.style.paddingTop = 8;
            titre.style.paddingBottom = 2;
            return titre;
        }

        // ------------------------------------------------------------- génération

        void Generer()
        {
            _carte = GenerateurCarte.Generer(_graine, _parametres, out _rapport);
            _regles = _carte != null && _parametres.PhaseFinale == PhaseGeneration.Complet
                ? Validation.Verifier(_carte, _parametres)
                : new List<ResultatRegle>();

            _apercu.Afficher(_carte, _parametres.Maillage.TailleCarte);
            RafraichirPanneaux();
        }

        void RafraichirPanneaux()
        {
            if (_carte == null)
            {
                _resume.text = $"ÉCHEC pour la graine {_graine}\n{_rapport?.MotifEchec}";
                _resume.style.color = new Color(0.95f, 0.45f, 0.35f);
            }
            else
            {
                int enfreintes = 0;
                foreach (ResultatRegle regle in _regles)
                {
                    if (!regle.Conforme) enfreintes++;
                }
                _resume.text = $"Graine {_graine} — {_rapport.MillisecondesTotal} ms" +
                               (_regles.Count > 0
                                   ? $"\n{_regles.Count - enfreintes}/{_regles.Count} règles conformes"
                                   : "\n(génération partielle : règles non vérifiées)");
                _resume.style.color = enfreintes == 0 ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.75f, 0.35f);
            }

            _panneauRegles.Clear();
            foreach (ResultatRegle regle in _regles)
            {
                var ligne = new Label($"{(regle.Conforme ? "✓" : "✗")}  {regle.Code}  {regle.Libelle}\n     {regle.Mesure}");
                ligne.style.whiteSpace = WhiteSpace.Normal;
                ligne.style.paddingLeft = 8;
                ligne.style.paddingBottom = 4;
                ligne.style.color = regle.Conforme ? new Color(0.75f, 0.85f, 0.75f) : new Color(0.95f, 0.5f, 0.4f);
                _panneauRegles.Add(ligne);
            }

            _panneauMesures.Clear();
            if (_rapport != null)
            {
                foreach (string ligne in DecrireMesures())
                {
                    var etiquette = new Label(ligne);
                    etiquette.style.whiteSpace = WhiteSpace.Normal;
                    etiquette.style.paddingLeft = 8;
                    etiquette.style.paddingBottom = 4;
                    _panneauMesures.Add(etiquette);
                }
            }
        }

        IEnumerable<string> DecrireMesures()
        {
            if (_rapport.Maillage != null) yield return "P1-P2 · " + _rapport.Maillage;
            if (_rapport.Ile != null) yield return "P3 · " + _rapport.Ile;
            if (_rapport.Relief != null) yield return "P4 · " + _rapport.Relief;
            if (_rapport.Hydrologie != null) yield return "P4 · " + _rapport.Hydrologie;
            if (_rapport.Zones != null) yield return "P5 · " + _rapport.Zones;
            if (_rapport.Groupes != null) yield return "P6 · " + _rapport.Groupes;
            if (_rapport.Passages != null) yield return "P7 · " + _rapport.Passages;
            if (_rapport.Materialisation != null) yield return "P8 · " + _rapport.Materialisation;
            if (_rapport.Departs != null) yield return "P9 · " + _rapport.Departs;
            if (_rapport.Terrains != null) yield return "P10 · " + _rapport.Terrains;
        }

        void AfficherSurvol(int cellule)
        {
            if (_carte == null || cellule < 0 || cellule >= _carte.NbCellules) return;

            if (!_carte.Terre[cellule])
            {
                _survol.text = $"Cellule {cellule} — mer (distance au rivage {_carte.DistCote?[cellule]})";
                return;
            }

            int zone = _carte.ZoneDeCellule != null ? _carte.ZoneDeCellule[cellule] : -1;
            var texte = $"Cellule {cellule} — terre, altitude {AltitudeCellule(cellule):F1} m";
            if (zone >= 0)
            {
                texte += $"\nZone {zone}";
                if (_carte.TerrainDeZone != null)
                {
                    texte += $" · {Terrains.Nom(_carte.TerrainDeZone[zone])} · {_carte.RessourceDeZone[zone]}";
                    if (_carte.ZoneRiveraine[zone]) texte += " + poisson";
                }
                if (_carte.GroupeDeZone != null) texte += $"\nGroupe {_carte.GroupeDeZone[zone]}";
            }
            if (_carte.Drapeaux != null)
            {
                var drapeaux = new List<string>();
                foreach (DrapeauxCellule drapeau in Enum.GetValues(typeof(DrapeauxCellule)))
                {
                    if (drapeau != DrapeauxCellule.Aucun && _carte.ADrapeau(cellule, drapeau)) drapeaux.Add(drapeau.ToString());
                }
                if (drapeaux.Count > 0) texte += "\n" + string.Join(", ", drapeaux);
            }
            _survol.text = texte;
        }

        float AltitudeCellule(int cellule)
        {
            if (_carte.HauteurCoin == null) return 0f;
            var g = _carte.Graphe;
            float somme = 0f;
            int n = g.DebutCoins[cellule + 1] - g.DebutCoins[cellule];
            for (int s = g.DebutCoins[cellule]; s < g.DebutCoins[cellule + 1]; s++)
            {
                somme += _carte.HauteurCoin[g.CoinsDeCellule[s]];
            }
            return n > 0 ? somme / n : 0f;
        }

        // --------------------------------------------------------------- campagne

        void LancerCampagne()
        {
            var parametres = new ParametresGeneration();
            ResultatCampagne resultat = null;
            try
            {
                resultat = CampagneGraines.Executer(_graine, _nbGrainesCampagne, parametres, true,
                    (avancement, message) => EditorUtility.DisplayProgressBar("Campagne de graines", message, avancement));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Map Heroic] Campagne de {_nbGrainesCampagne} graines\n{resultat}");
            EditorUtility.DisplayDialog("Campagne terminée", resultat.ToString(), "Fermer");
        }
    }
}
