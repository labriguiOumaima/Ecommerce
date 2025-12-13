using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;

namespace project.Controllers
{
    public class CustomController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<CustomController> _logger;

        public CustomController(Neo4jService neo4jService, ILogger<CustomController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // GET: Custom/Index - Page de sélection des catégories
        public IActionResult Index()
        {
            return View();
        }

        // GET: Custom/Customize?category=Cake
        public IActionResult Customize(string category)
        {
            if (string.IsNullOrEmpty(category))
            {
                return RedirectToAction("Index");
            }

            ViewBag.Category = category;
            return View();
        }

        // POST: Custom/SubmitCustomOrder
        [HttpPost]
        public async Task<IActionResult> SubmitCustomOrder(DemandeCustom demande)
        {
            try
            {
                // Récupérer l'ID du client depuis la session
                var clientIdString = HttpContext.Session.GetString("ClientId");
                if (string.IsNullOrEmpty(clientIdString))
                {
                    TempData["Error"] = "Vous devez être connecté pour passer une commande personnalisée.";
                    return RedirectToAction("Login", "Auth");
                }

                demande.ClientId = int.Parse(clientIdString);
                demande.DateCreation = DateTime.Now;
                demande.Statut = "En attente de prix";

                var session = _neo4jService.GetAsyncSession();

                // Récupérer les infos client
                var clientQuery = @"
                    MATCH (c:Client)
                    WHERE c.id = $clientId
                    RETURN c.nom as Nom, c.prenom as Prenom, c.email as Email, c.telephone as Telephone
                    LIMIT 1";

                var clientResult = await session.RunAsync(clientQuery, new { clientId = demande.ClientId });

                if (await clientResult.FetchAsync())
                {
                    var clientRecord = clientResult.Current;
                    demande.ClientNom = clientRecord["Nom"].As<string>();
                    demande.ClientPrenom = clientRecord["Prenom"].As<string>();
                    demande.ClientEmail = clientRecord["Email"].As<string>();
                    demande.ClientTelephone = clientRecord["Telephone"].As<string>();
                }

                // Créer la demande custom dans Neo4j
                var query = @"
                    MATCH (c:Client {id: $clientId})
                    CREATE (d:DemandeCustom {
                        id: randomUUID(),
                        clientId: $clientId,
                        categorie: $categorie,
                        spongeFilling: $spongeFilling,
                        quantite: $quantite,
                        message: $message,
                        statut: $statut,
                        dateCreation: datetime(),
                        clientNom: $clientNom,
                        clientPrenom: $clientPrenom,
                        clientEmail: $clientEmail,
                        clientTelephone: $clientTelephone
                    })
                    CREATE (c)-[:A_DEMANDE]->(d)
                    RETURN d";

                await session.RunAsync(query, new
                {
                    clientId = demande.ClientId,
                    categorie = demande.Categorie,
                    spongeFilling = demande.SpongeFilling,
                    quantite = demande.Quantite,
                    message = demande.Message ?? "",
                    statut = demande.Statut,
                    clientNom = demande.ClientNom ?? "",
                    clientPrenom = demande.ClientPrenom ?? "",
                    clientEmail = demande.ClientEmail ?? "",
                    clientTelephone = demande.ClientTelephone ?? ""
                });

                await session.DisposeAsync();

                TempData["Success"] = "Votre demande personnalisée a été envoyée avec succès ! Nous vous contacterons bientôt avec un devis.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la création de la demande custom: {ex.Message}");
                TempData["Error"] = "Une erreur s'est produite lors de l'envoi de votre demande.";
                return RedirectToAction("Customize", new { category = demande.Categorie });
            }
        }
    }
}
