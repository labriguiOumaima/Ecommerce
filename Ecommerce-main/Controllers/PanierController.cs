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
            // Récupérer le panier de la session
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
                // Vérifier si l'utilisateur est connecté
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    TempData["Error"] = "Vous devez être connecté pour ajouter au panier";
                    return RedirectToAction("Details", "Gateau", new { id = gateauId });
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer les infos du gâteau depuis Neo4j
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

                // Calculer le prix selon le nombre de personnes
                var prixUnitaire = prixBase * nombrePersonnes / 6;

                // Récupérer le panier de la session
                var panierJson = HttpContext.Session.GetString("Panier");
                var panier = string.IsNullOrEmpty(panierJson)
                    ? new List<ArticlePanier>()
                    : JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson) ?? new List<ArticlePanier>();

                // Vérifier si le gâteau existe déjà dans le panier (même gâteau, même nombre de personnes)
                var existingItem = panier.FirstOrDefault(p => p.GateauId == gateauId && p.NombrePersonnes == nombrePersonnes);

                if (existingItem != null)
                {
                    // Augmenter la quantité
                    existingItem.Quantite += quantite;
                }
                else
                {
                    // Ajouter un nouvel article
                    panier.Add(new ArticlePanier
                    {
                        GateauId = gateauId,
                        NomGateau = nomGateau,
                        Quantite = quantite,
                        NombrePersonnes = nombrePersonnes,
                        PrixUnitaire = prixUnitaire
                    });
                }

                // Sauvegarder le panier en session
                var updatedPanierJson = JsonSerializer.Serialize(panier);
                HttpContext.Session.SetString("Panier", updatedPanierJson);

                // Mettre à jour le compteur (nombre total d'articles)
                var totalArticles = panier.Sum(p => p.Quantite);
                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                await session.DisposeAsync();

                // Message de succès
                TempData["Success"] = "Ajouté au panier";
                TempData["GateauNom"] = nomGateau;

                _logger.LogInformation($"Gâteau {nomGateau} ajouté au panier. Total articles: {totalArticles}");

                // RESTER sur la page Details (ne pas rediriger vers le panier)
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
                // Récupérer le panier
                var panierJson = HttpContext.Session.GetString("Panier");
                if (string.IsNullOrEmpty(panierJson))
                {
                    return RedirectToAction(nameof(Index));
                }

                var panier = JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson) ?? new List<ArticlePanier>();

                // Retirer l'article
                var item = panier.FirstOrDefault(p => p.GateauId == gateauId && p.NombrePersonnes == nombrePersonnes);
                if (item != null)
                {
                    panier.Remove(item);
                    TempData["Success"] = $"{item.NomGateau} retiré du panier";
                }

                // Sauvegarder
                var updatedPanierJson = JsonSerializer.Serialize(panier);
                HttpContext.Session.SetString("Panier", updatedPanierJson);

                // Mettre à jour le compteur
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
    }
}