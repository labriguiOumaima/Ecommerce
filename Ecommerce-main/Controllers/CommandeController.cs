using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using System.Text.Json;
using Neo4j.Driver;

namespace project.Controllers
{
    public class CommandeController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<CommandeController> _logger;

        public CommandeController(Neo4jService neo4jService, ILogger<CommandeController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // POST: Commande/Valider
        [HttpPost]
        public async Task<IActionResult> Valider()
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    TempData["Error"] = "Vous devez être connecté pour valider une commande";
                    return RedirectToAction("Index", "Panier");
                }

                var panierJson = HttpContext.Session.GetString("Panier");
                if (string.IsNullOrEmpty(panierJson))
                {
                    TempData["Error"] = "Votre panier est vide";
                    return RedirectToAction("Index", "Panier");
                }

                var panier = JsonSerializer.Deserialize<List<ArticlePanier>>(panierJson);
                if (panier == null || !panier.Any())
                {
                    TempData["Error"] = "Votre panier est vide";
                    return RedirectToAction("Index", "Panier");
                }

                var session = _neo4jService.GetAsyncSession();

                // Vérifier si le client existe
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Client introuvable";
                    return RedirectToAction("Index", "Panier");
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Générer ID unique commande
                var commandeIdQuery = @"
                    OPTIONAL MATCH (cmd:Commande)
                    WITH MAX(cmd.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                var commandeIdResult = await session.RunAsync(commandeIdQuery);
                int commandeId = 1;

                if (await commandeIdResult.FetchAsync())
                    commandeId = commandeIdResult.Current["NextId"].As<int>();

                _logger.LogInformation($"ID généré pour la commande: {commandeId}");

                // Total commande
                var total = panier.Sum(p => p.PrixUnitaire * p.Quantite);
                var now = DateTime.Now;

                // Créer la commande
                var createCommandeQuery = @"
                    MATCH (c:Client {id: $clientId})
                    CREATE (cmd:Commande {
                        id: $commandeId,
                        date: $dateCommande,
                        total: $total,
                        statut: 'En attente'
                    })
                    CREATE (c)-[:A_PASSE]->(cmd)
                    RETURN cmd.id as CommandeId, cmd.date as Date";

                var createResult = await session.RunAsync(createCommandeQuery, new
                {
                    clientId = clientId,
                    commandeId = commandeId,
                    dateCommande = now,
                    total = total
                });

                await createResult.FetchAsync();

                // Ajouter les articles du panier
                foreach (var item in panier)
                {
                    var addArticleQuery = @"
                        MATCH (cmd:Commande {id: $commandeId})
                        MATCH (g:Gateau {id: $gateauId})
                        CREATE (cmd)-[:CONTIENT {
                            quantite: $quantite,
                            nombrePersonnes: $nombrePersonnes,
                            prixUnitaire: $prixUnitaire
                        }]->(g)";

                    await session.RunAsync(addArticleQuery, new
                    {
                        commandeId = commandeId,
                        gateauId = item.GateauId,
                        quantite = item.Quantite,
                        nombrePersonnes = item.NombrePersonnes,
                        prixUnitaire = item.PrixUnitaire
                    });
                }

                await session.DisposeAsync();

                // Vider le panier
                HttpContext.Session.Remove("Panier");
                HttpContext.Session.Remove("PanierCount");

                _logger.LogInformation($"Commande #{commandeId} créée pour le client {clientEmail}");

                return RedirectToAction("Confirmation", new { id = commandeId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur validation commande: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                TempData["Error"] = "Erreur lors de la validation de la commande";
                return RedirectToAction("Index", "Panier");
            }
        }

        // GET: Commande/Confirmation/{id}
        public async Task<IActionResult> Confirmation(int id)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return RedirectToAction("Index", "Gateau");
                }

                var session = _neo4jService.GetAsyncSession();

                var commandeQuery = @"
                    MATCH (c:Client {email: $email})-[:A_PASSE]->(cmd:Commande {id: $commandeId})
                    RETURN cmd.id as Id, 
                           cmd.date as DateCommande, 
                           cmd.total as MontantTotal, 
                           cmd.statut as Statut,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           c.email as ClientEmail,
                           c.telephone as ClientTelephone";

                var commandeResult = await session.RunAsync(commandeQuery, new
                {
                    email = clientEmail,
                    commandeId = id
                });

                if (!await commandeResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Commande introuvable";
                    return RedirectToAction("Index", "Gateau");
                }

                var record = commandeResult.Current;

                var commande = new Commande
                {
                    Id = record["Id"].As<int>(),
                    DateCommande = record["DateCommande"].As<DateTime>(),
                    MontantTotal = record["MontantTotal"].As<decimal>(),
                    Statut = record["Statut"].As<string>(),
                    ClientNom = record["ClientNom"].As<string>(),
                    ClientPrenom = record["ClientPrenom"].As<string>(),
                    ClientEmail = record["ClientEmail"].As<string>(),
                    ClientTelephone = record["ClientTelephone"].As<string>()
                };

                var articlesQuery = @"
                    MATCH (cmd:Commande {id: $commandeId})-[r:CONTIENT]->(g:Gateau)
                    RETURN g.id as GateauId,
                           g.nom as NomGateau,
                           r.quantite as Quantite,
                           r.nombrePersonnes as NombrePersonnes,
                           r.prixUnitaire as PrixUnitaire";

                var articlesResult = await session.RunAsync(articlesQuery, new { commandeId = id });

                await foreach (var a in articlesResult)
                {
                    commande.Articles.Add(new ArticleCommande
                    {
                        GateauId = a["GateauId"].As<int>(),
                        NomGateau = a["NomGateau"].As<string>(),
                        Quantite = a["Quantite"].As<int>(),
                        NombrePersonnes = a["NombrePersonnes"].As<int>(),
                        PrixUnitaire = a["PrixUnitaire"].As<decimal>()
                    });
                }

                await session.DisposeAsync();

                return View(commande);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur confirmation commande: {ex.Message}");
                TempData["Error"] = "Erreur lors de la récupération de la commande";
                return RedirectToAction("Index", "Gateau");
            }
        }
    }
}
