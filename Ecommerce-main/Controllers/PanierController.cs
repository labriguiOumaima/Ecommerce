using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using System.Text.Json;
using Neo4j.Driver;

namespace project.Controllers
{
    public class PanierController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<PanierController> _logger;

        public PanierController(Neo4jService neo4jService, ILogger<PanierController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // GET: Panier/Index - Afficher le panier
        public IActionResult Index()
        {
            var panierJson = HttpContext.Session.GetString("Panier");
            var panier = string.IsNullOrEmpty(panierJson)
                ? new List<ArticlePanier>()
                : JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson) ?? new List<ArticlePanier>();

            return View(panier);
        }

        // POST: Panier/Ajouter
        [HttpPost]
        public async Task<IActionResult> Ajouter(int gateauId, int quantite, int nombrePersonnes)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    TempData["Error"] = "Vous devez être connecté pour ajouter au panier";
                    return RedirectToAction("Details", "Gateau", new { id = gateauId });
                }

                var session = _neo4jService.GetAsyncSession();

                var gateauQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $gateauId
                    RETURN g.nom as Nom, g.prix as Prix";

                var gateauResult = await session.RunAsync(gateauQuery, new { gateauId });

                if (!await gateauResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Gâteau introuvable";
                    return RedirectToAction("Details", "Gateau", new { id = gateauId });
                }

                var record = gateauResult.Current;
                var nomGateau = record["Nom"].As<string>();
                var prixBase = record["Prix"].As<decimal>();
                decimal ratio = nombrePersonnes switch
                {
                    6 => 1m,
                    10 => 1.6m,
                    15 => 2.4m,
                    20 => 3.2m,
                    30 => 4.8m,
                    _ => nombrePersonnes / 6m
                };
                var prixUnitaire = prixBase * ratio;

                var panierJson = HttpContext.Session.GetString("Panier");
                var panier = string.IsNullOrEmpty(panierJson)
                    ? new List<ArticlePanier>()
                    : JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson) ?? new List<ArticlePanier>();

                var existingItem = panier.FirstOrDefault(p => p.GateauId == gateauId && p.NombrePersonnes == nombrePersonnes);

                if (existingItem != null)
                {
                    existingItem.Quantite += quantite;
                }
                else
                {
                    panier.Add(new ArticlePanier
                    {
                        GateauId = gateauId,
                        NomGateau = nomGateau,
                        Quantite = quantite,
                        NombrePersonnes = nombrePersonnes,
                        PrixUnitaire = prixUnitaire,  // ← VIRGULE AJOUTÉE
                        PrixBase = prixBase
                    });
                }

                var updatedPanierJson = JsonSerializer.Serialize(panier);
                HttpContext.Session.SetString("Panier", updatedPanierJson);

                var totalArticles = panier.Sum(p => p.Quantite);
                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                await session.DisposeAsync();

                TempData["Success"] = "Ajouté au panier";
                TempData["GateauNom"] = nomGateau;

                _logger.LogInformation($"Gâteau {nomGateau} ajouté au panier. Total articles: {totalArticles}");

                return RedirectToAction("Details", "Gateau", new { id = gateauId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ajout panier: {ex.Message}");
                TempData["Error"] = "Erreur lors de l'ajout au panier";
                return RedirectToAction("Details", "Gateau", new { id = gateauId });
            }
        }

        // POST: Panier/Retirer
        [HttpPost]
        public IActionResult Retirer(int gateauId, int nombrePersonnes)
        {
            try
            {
                var panierJson = HttpContext.Session.GetString("Panier");
                if (string.IsNullOrEmpty(panierJson))
                {
                    return RedirectToAction(nameof(Index));
                }

                var panier = JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson) ?? new List<ArticlePanier>();

                var item = panier.FirstOrDefault(p => p.GateauId == gateauId && p.NombrePersonnes == nombrePersonnes);
                if (item != null)
                {
                    panier.Remove(item);
                    TempData["Success"] = $"{item.NomGateau} retiré du panier";
                }

                var updatedPanierJson = JsonSerializer.Serialize(panier);
                HttpContext.Session.SetString("Panier", updatedPanierJson);

                var totalArticles = panier.Sum(p => p.Quantite);
                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur retrait panier: {ex.Message}");
                TempData["Error"] = "Erreur lors du retrait";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Panier/MettreAJourAjax
        [HttpPost]
        public async Task<IActionResult> MettreAJourAjax([FromBody] UpdatePanierRequest request)
        {
            try
            {
                var session = _neo4jService.GetAsyncSession();
                var panierJson = HttpContext.Session.GetString("Panier");

                if (string.IsNullOrEmpty(panierJson))
                {
                    return Json(new { success = false });
                }

                var panier = JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson) ?? new List<ArticlePanier>();
                var oldItem = panier.FirstOrDefault(p => p.GateauId == request.GateauId && p.NombrePersonnes == request.AncienNombrePersonnes);

                if (oldItem != null)
                {
                    var gateauQuery = @"MATCH (g:Gateau) WHERE g.id = $gateauId RETURN g.nom as Nom, g.prix as Prix";
                    var gateauResult = await session.RunAsync(gateauQuery, new { gateauId = request.GateauId });

                    if (await gateauResult.FetchAsync())
                    {
                        var record = gateauResult.Current;
                        var nomGateau = record["Nom"].As<string>();
                        var prixBase = record["Prix"].As<decimal>();
                        decimal ratio = request.NombrePersonnes switch
                        {
                            6 => 1m,
                            10 => 1.6m,
                            15 => 2.4m,
                            20 => 3.2m,
                            30 => 4.8m,
                            _ => request.NombrePersonnes / 6m
                        };
                        var nouveauPrixUnitaire = prixBase * ratio;

                        panier.Remove(oldItem);

                        var existingItem = panier.FirstOrDefault(p => p.GateauId == request.GateauId && p.NombrePersonnes == request.NombrePersonnes);
                        if (existingItem != null)
                        {
                            existingItem.Quantite = request.Quantite;
                        }
                        else
                        {
                            panier.Add(new ArticlePanier
                            {
                                GateauId = request.GateauId,
                                NomGateau = nomGateau,
                                Quantite = request.Quantite,
                                NombrePersonnes = request.NombrePersonnes,
                                PrixUnitaire = nouveauPrixUnitaire,  // ← VIRGULE AJOUTÉE
                                PrixBase = prixBase
                            });
                        }
                    }
                }

                HttpContext.Session.SetString("Panier", JsonSerializer.Serialize(panier));
                HttpContext.Session.SetString("PanierCount", panier.Sum(p => p.Quantite).ToString());

                await session.DisposeAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur mise à jour AJAX: {ex.Message}");
                return Json(new { success = false });
            }
        }
    }

    // Classe pour la requête AJAX
    public class UpdatePanierRequest
    {
        public int GateauId { get; set; }
        public int AncienNombrePersonnes { get; set; }
        public int NombrePersonnes { get; set; }
        public int Quantite { get; set; }
    }
}