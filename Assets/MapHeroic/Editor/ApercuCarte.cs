using System;
using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapHeroic.EditeurCarte
{
    /// <summary>Ce que l'aperçu peint dans les cellules.</summary>
    public enum ModeRemplissage
    {
        Terrain,
        Zones,
        Groupes,
        Altitude,
        Durete
    }

    [Flags]
    public enum Calques
    {
        Aucun = 0,
        ContoursZones = 1,
        ContoursGroupes = 2,
        Reliefs = 4,
        Eau = 8,
        Passages = 16,
        Ponts = 32,
        Departs = 64,
        Sockets = 128,
        CellulesFines = 256
    }

    /// <summary>
    /// Aperçu 2D de la carte, dessiné au vectoriel.
    ///
    /// C'est l'outil qui rend le générateur inspectable : chaque garantie a un calque qui la
    /// montre, de sorte qu'un défaut se voit avant même de lire les chiffres du panneau de
    /// validation. Les frontières de groupes sont épaisses et les passages verts, pour qu'on
    /// vérifie d'un regard qu'aucun groupe n'est enfermé.
    /// </summary>
    public sealed class ApercuCarte : VisualElement
    {
        Carte _carte;
        float _tailleCarte = 1400f;
        float _altitudeMax = 20f;

        public ModeRemplissage Mode = ModeRemplissage.Terrain;
        public Calques CalquesActifs = Calques.ContoursZones | Calques.ContoursGroupes | Calques.Reliefs
                                     | Calques.Eau | Calques.Passages | Calques.Departs;

        public event Action<int> CelluleSurvolee;

        public ApercuCarte()
        {
            style.flexGrow = 1;
            style.minHeight = 420;
            style.backgroundColor = PaletteCarte.Fond;
            generateVisualContent += Dessiner;

            RegisterCallback<PointerMoveEvent>(evenement =>
            {
                int cellule = CelluleSous(evenement.localPosition);
                if (cellule >= 0) CelluleSurvolee?.Invoke(cellule);
            });
        }

        public void Afficher(Carte carte, float tailleCarte)
        {
            _carte = carte;
            _tailleCarte = tailleCarte;
            _altitudeMax = 1f;
            if (carte?.HauteurCoin != null)
            {
                foreach (float h in carte.HauteurCoin) _altitudeMax = math.max(_altitudeMax, h);
            }
            MarkDirtyRepaint();
        }

        // ------------------------------------------------------------- projection

        float Echelle => math.min(contentRect.width, contentRect.height) / math.max(1f, _tailleCarte);

        Vector2 Projeter(float2 point)
        {
            float e = Echelle;
            float margeX = (contentRect.width - _tailleCarte * e) * 0.5f;
            float margeY = (contentRect.height - _tailleCarte * e) * 0.5f;
            // L'axe Y est inversé : la carte a son origine en bas, l'écran en haut.
            return new Vector2(margeX + point.x * e, margeY + (_tailleCarte - point.y) * e);
        }

        int CelluleSous(Vector2 position)
        {
            if (_carte?.Graphe == null) return -1;
            float e = Echelle;
            if (e <= 0f) return -1;

            float margeX = (contentRect.width - _tailleCarte * e) * 0.5f;
            float margeY = (contentRect.height - _tailleCarte * e) * 0.5f;
            var monde = new float2((position.x - margeX) / e, _tailleCarte - (position.y - margeY) / e);

            int meilleure = -1;
            float meilleureDistance = float.MaxValue;
            GrapheCellules g = _carte.Graphe;
            for (int c = 0; c < g.NbCellules; c++)
            {
                float d = math.distancesq(g.Sites[c], monde);
                if (d < meilleureDistance) { meilleureDistance = d; meilleure = c; }
            }
            return meilleure;
        }

        // --------------------------------------------------------------- dessin

        void Dessiner(MeshGenerationContext contexte)
        {
            if (_carte?.Graphe == null) return;
            Painter2D peintre = contexte.painter2D;
            GrapheCellules g = _carte.Graphe;

            RemplirCellules(peintre, g);
            if ((CalquesActifs & Calques.CellulesFines) != 0) TracerCellulesFines(peintre, g);
            if ((CalquesActifs & Calques.Eau) != 0) TracerEau(peintre, g);
            if ((CalquesActifs & Calques.ContoursZones) != 0) TracerFrontieres(peintre, g, false);
            if ((CalquesActifs & Calques.ContoursGroupes) != 0) TracerFrontieres(peintre, g, true);
            if ((CalquesActifs & Calques.Passages) != 0) TracerPassages(peintre, g);
            if ((CalquesActifs & Calques.Ponts) != 0) TracerPonts(peintre);
            if ((CalquesActifs & Calques.Sockets) != 0) TracerSockets(peintre, g);
            if ((CalquesActifs & Calques.Departs) != 0) TracerDeparts(peintre, g);
        }

        void RemplirCellules(Painter2D peintre, GrapheCellules g)
        {
            bool reliefs = (CalquesActifs & Calques.Reliefs) != 0;

            for (int c = 0; c < g.NbCellules; c++)
            {
                peintre.fillColor = CouleurCellule(c, reliefs);
                peintre.BeginPath();
                int debut = g.DebutCoins[c];
                peintre.MoveTo(Projeter(g.Coins[g.CoinsDeCellule[debut]]));
                for (int s = debut + 1; s < g.DebutCoins[c + 1]; s++)
                {
                    peintre.LineTo(Projeter(g.Coins[g.CoinsDeCellule[s]]));
                }
                peintre.ClosePath();
                peintre.Fill();
            }
        }

        Color CouleurCellule(int cellule, bool reliefs)
        {
            if (!_carte.Terre[cellule])
            {
                return _carte.DistCote != null && _carte.DistCote[cellule] >= -2
                    ? PaletteCarte.HautsFonds
                    : PaletteCarte.Mer;
            }

            if (reliefs && _carte.Drapeaux != null)
            {
                if (_carte.ADrapeau(cellule, DrapeauxCellule.Lac)) return PaletteCarte.Lac;
                if (_carte.ADrapeau(cellule, DrapeauxCellule.Massif))
                {
                    float h = HauteurMoyenne(cellule);
                    return h > _altitudeMax * 0.8f ? PaletteCarte.NeigeMassif : PaletteCarte.Massif;
                }
            }

            int zone = _carte.ZoneDeCellule != null ? _carte.ZoneDeCellule[cellule] : -1;

            switch (Mode)
            {
                case ModeRemplissage.Terrain:
                    if (zone >= 0 && _carte.TerrainDeZone != null) return PaletteCarte.Terrain(_carte.TerrainDeZone[zone]);
                    if (zone >= 0) return PaletteCarte.Zone(zone);
                    return PaletteCarte.Altitude(HauteurMoyenne(cellule), _altitudeMax);

                case ModeRemplissage.Zones:
                    return zone >= 0 ? PaletteCarte.Zone(zone) : PaletteCarte.Altitude(HauteurMoyenne(cellule), _altitudeMax);

                case ModeRemplissage.Groupes:
                    if (zone >= 0 && _carte.GroupeDeZone != null) return PaletteCarte.Groupe(_carte.GroupeDeZone[zone]);
                    return PaletteCarte.Altitude(HauteurMoyenne(cellule), _altitudeMax);

                case ModeRemplissage.Durete:
                    return _carte.Crete != null
                        ? Color.Lerp(new Color(0.20f, 0.30f, 0.20f), new Color(0.95f, 0.35f, 0.25f), _carte.Crete[cellule])
                        : Color.grey;

                default:
                    return PaletteCarte.Altitude(HauteurMoyenne(cellule), _altitudeMax);
            }
        }

        float HauteurMoyenne(int cellule)
        {
            if (_carte.HauteurCoin == null) return 0f;
            GrapheCellules g = _carte.Graphe;
            float somme = 0f;
            int n = g.DebutCoins[cellule + 1] - g.DebutCoins[cellule];
            for (int s = g.DebutCoins[cellule]; s < g.DebutCoins[cellule + 1]; s++)
            {
                somme += _carte.HauteurCoin[g.CoinsDeCellule[s]];
            }
            return n > 0 ? somme / n : 0f;
        }

        void TracerCellulesFines(Painter2D peintre, GrapheCellules g)
        {
            peintre.strokeColor = new Color(0f, 0f, 0f, 0.12f);
            peintre.lineWidth = 1f;
            peintre.BeginPath();
            for (int e = 0; e < g.NbAretes; e++)
            {
                peintre.MoveTo(Projeter(g.Coins[g.AreteCoinA[e]]));
                peintre.LineTo(Projeter(g.Coins[g.AreteCoinB[e]]));
            }
            peintre.Stroke();
        }

        /// <summary>Contours de zones (fins) ou de groupes (épais).</summary>
        void TracerFrontieres(Painter2D peintre, GrapheCellules g, bool groupes)
        {
            if (_carte.ZoneDeCellule == null) return;
            if (groupes && _carte.GroupeDeZone == null) return;

            peintre.strokeColor = groupes ? PaletteCarte.ContourGroupe : PaletteCarte.ContourZone;
            peintre.lineWidth = groupes ? 2.5f : 1f;
            peintre.BeginPath();

            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !_carte.Terre[a] || !_carte.Terre[b]) continue;
                int za = _carte.ZoneDeCellule[a], zb = _carte.ZoneDeCellule[b];
                if (za < 0 || zb < 0) continue;

                bool separe = groupes
                    ? _carte.GroupeDeZone[za] != _carte.GroupeDeZone[zb]
                    : za != zb;
                if (!separe) continue;

                peintre.MoveTo(Projeter(g.Coins[g.AreteCoinA[e]]));
                peintre.LineTo(Projeter(g.Coins[g.AreteCoinB[e]]));
            }
            peintre.Stroke();
        }

        void TracerEau(Painter2D peintre, GrapheCellules g)
        {
            if (_carte.TypeArete == null) return;

            foreach (var (type, couleur, epaisseur) in new[]
            {
                (TypeFrontiere.RiviereProlongee, PaletteCarte.RiviereProlongee, 2f),
                (TypeFrontiere.Riviere, PaletteCarte.Riviere, 3.5f)
            })
            {
                peintre.strokeColor = couleur;
                peintre.lineWidth = epaisseur;
                peintre.BeginPath();
                for (int e = 0; e < g.NbAretes; e++)
                {
                    if (_carte.TypeArete[e] != type) continue;
                    peintre.MoveTo(Projeter(g.Coins[g.AreteCoinA[e]]));
                    peintre.LineTo(Projeter(g.Coins[g.AreteCoinB[e]]));
                }
                peintre.Stroke();
            }
        }

        void TracerPassages(Painter2D peintre, GrapheCellules g)
        {
            if (_carte.Passages == null) return;

            peintre.strokeColor = PaletteCarte.Passage;
            peintre.lineWidth = 4f;
            peintre.BeginPath();
            foreach (Passage passage in _carte.Passages)
            {
                foreach (int e in passage.AretesFines)
                {
                    peintre.MoveTo(Projeter(g.Coins[g.AreteCoinA[e]]));
                    peintre.LineTo(Projeter(g.Coins[g.AreteCoinB[e]]));
                }
            }
            peintre.Stroke();

            peintre.fillColor = PaletteCarte.Passage;
            foreach (Passage passage in _carte.Passages) Pastille(peintre, passage.Position, 3.5f);
        }

        void TracerPonts(Painter2D peintre)
        {
            if (_carte.Ponts == null) return;
            peintre.fillColor = PaletteCarte.Pont;
            foreach (EmplacementPont pont in _carte.Ponts) Pastille(peintre, pont.Position, 3f);
        }

        void TracerSockets(Painter2D peintre, GrapheCellules g)
        {
            if (_carte.Drapeaux == null) return;

            peintre.fillColor = PaletteCarte.SocketMine;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (_carte.ADrapeau(c, DrapeauxCellule.SocketMine)) Pastille(peintre, g.Sites[c], 3f);
            }

            peintre.fillColor = PaletteCarte.SocketPeche;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (_carte.ADrapeau(c, DrapeauxCellule.SocketPeche)) Pastille(peintre, g.Sites[c], 2f);
            }
        }

        void TracerDeparts(Painter2D peintre, GrapheCellules g)
        {
            if (_carte.ZonesDepart == null) return;

            peintre.fillColor = PaletteCarte.Depart;
            peintre.strokeColor = Color.black;
            peintre.lineWidth = 1.5f;

            foreach (int zone in _carte.ZonesDepart)
            {
                if (zone < 0) continue;
                float2 centre = float2.zero;
                float poids = 0f;
                foreach (int c in _carte.CellulesDeZone[zone])
                {
                    float a = g.Aire[c];
                    centre += g.Sites[c] * a;
                    poids += a;
                }
                if (poids <= 0f) continue;
                Pastille(peintre, centre / poids, 7f, true);
            }
        }

        void Pastille(Painter2D peintre, float2 position, float rayon, bool contour = false)
        {
            Vector2 centre = Projeter(position);
            peintre.BeginPath();
            peintre.Arc(centre, rayon, 0f, 360f);
            peintre.ClosePath();
            peintre.Fill();
            if (contour) peintre.Stroke();
        }
    }
}
