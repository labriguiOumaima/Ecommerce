using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;

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

        // GET: Client/Panier - Afficher le panier
        public IActionResult Index()
        {
            // TODO: Récupérer le panier du client connecté depuis Neo4j
            return View();
        }

        // POST: Client/Panier/Ajouter
        [HttpPost]
        public IActionResult Ajouter(int gateauId, int quantite = 1)
        {
            // TODO: Ajouter un gâteau au panier dans Neo4j
            return RedirectToAction(nameof(Index));
        }

        // POST: Client/Panier/Retirer
        [HttpPost]
        public IActionResult Retirer(int gateauId)
        {
            // TODO: Retirer un gâteau du panier
            return RedirectToAction(nameof(Index));
        }
    }
}

