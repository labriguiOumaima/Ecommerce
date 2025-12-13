using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;
using System.Security.Cryptography;
using System.Text;

namespace project.Controllers
{
    public class AuthController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(Neo4jService neo4jService, ILogger<AuthController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        // POST: Auth/Register
        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return BadRequest(string.Join(", ", errors));
                }

                var session = _neo4jService.GetAsyncSession();

                // Vérifier si l'email existe déjà
                var checkQuery = "MATCH (c:Client {email: $email}) RETURN c";
                var checkResult = await session.RunAsync(checkQuery, new { email = model.Email });

                if (await checkResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return BadRequest("Un compte avec cet email existe déjà");
                }

                // Générer un ID unique pour le nouveau client
                var getMaxIdQuery = @"
                    OPTIONAL MATCH (c:Client)
                    WITH MAX(c.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                var maxIdResult = await session.RunAsync(getMaxIdQuery);
                await maxIdResult.FetchAsync();
                var newClientId = maxIdResult.Current["NextId"].As<int>();

                // Créer le nouveau client avec mot de passe hashé
                var hashedPassword = HashPassword(model.Mdp);

                var query = @"
                    CREATE (c:Client {
                        id: $id,
                        nom: $nom,
                        prenom: $prenom,
                        email: $email,
                        mdp: $mdp,
                        telephone: $telephone,
                        adresse: $adresse,
                        dateInscription: datetime()
                    })
                    RETURN c.id as ClientId, c.nom as Nom, c.prenom as Prenom, c.email as Email";

                var result = await session.RunAsync(query, new
                {
                    id = newClientId,
                    nom = model.Nom,
                    prenom = model.Prenom,
                    email = model.Email,
                    mdp = hashedPassword,
                    telephone = model.Telephone,
                    adresse = ""
                });

                if (await result.FetchAsync())
                {
                    var record = result.Current;
                    var clientId = record["ClientId"].As<int>();

                    // Enregistrer l'utilisateur dans la session
                    HttpContext.Session.SetInt32("ClientId", clientId);
                    HttpContext.Session.SetString("ClientEmail", model.Email);
                    HttpContext.Session.SetString("ClientName", $"{model.Prenom} {model.Nom}");

                    _logger.LogInformation($"Nouveau client créé : {model.Email} avec ID {clientId}");
                }

                await session.DisposeAsync();
                return Ok(new { message = "Inscription réussie" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur inscription: {ex.Message}");
                return BadRequest($"Erreur: {ex.Message}");
            }
        }

        // POST: Auth/Login
        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return BadRequest(string.Join(", ", errors));
                }

                var session = _neo4jService.GetAsyncSession();

                var hashedPassword = HashPassword(model.Mdp);

                var query = @"
                    MATCH (c:Client {email: $email, mdp: $mdp})
                    RETURN c.id as ClientId, c.nom as Nom, c.prenom as Prenom, c.email as Email
                    LIMIT 1";

                var result = await session.RunAsync(query, new
                {
                    email = model.Email,
                    mdp = hashedPassword
                });

                if (!await result.FetchAsync())
                {
                    await session.DisposeAsync();
                    return BadRequest("Email ou mot de passe incorrect");
                }

                var record = result.Current;
                var clientId = record["ClientId"].As<int>();

                // Enregistrer l'utilisateur dans la session
                HttpContext.Session.SetInt32("ClientId", clientId);
                HttpContext.Session.SetString("ClientEmail", model.Email);
                HttpContext.Session.SetString("ClientName", $"{record["Prenom"].As<string>()} {record["Nom"].As<string>()}");

                _logger.LogInformation($"Connexion réussie : {model.Email}");

                await session.DisposeAsync();
                return Ok(new { message = "Connexion réussie" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur connexion: {ex.Message}");
                return BadRequest($"Erreur: {ex.Message}");
            }
        }

        // POST: Auth/Logout
        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            _logger.LogInformation("Utilisateur déconnecté");
            return RedirectToAction("Index", "Gateau");
        }
    }
}