Imports System.Security.Claims
Imports Dapper
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.RateLimiting
Imports Microsoft.Extensions.Configuration

<ApiController>
<Route("api/auth")>
Public Class AuthController
    Inherits ControllerBase

    Private Const EchecsAvantBlocage As Integer = 5
    Private Shared ReadOnly DureeBlocage As TimeSpan = TimeSpan.FromMinutes(15)

    Private ReadOnly _base As Base
    Private ReadOnly _jetons As Jetons
    Private ReadOnly _cfg As IConfiguration

    Public Sub New(base As Base, jetons As Jetons, cfg As IConfiguration)
        _base = base
        _jetons = jetons
        _cfg = cfg
    End Sub

    ''' <summary>Création d'un compte enseignant. Nécessite le code d'invitation défini dans la configuration.</summary>
    <HttpPost("enseignant/inscription")>
    <EnableRateLimiting("connexion")>
    Public Async Function Inscription(d As InscriptionEnseignant) As Task(Of IActionResult)
        Dim codeAttendu = _cfg("Inscription:CodeInvitation")
        If String.IsNullOrWhiteSpace(codeAttendu) Then
            Return StatusCode(403, New Erreur("Les inscriptions sont fermées sur ce serveur."))
        End If
        If Not Jetons.Egaux(d.CodeInvitation, codeAttendu) Then
            Return StatusCode(403, New Erreur("Code d'invitation incorrect."))
        End If
        Dim email = If(d.Email, "").Trim().ToLowerInvariant()
        Dim nom = If(d.Nom, "").Trim()
        If Not email.Contains("@"c) OrElse email.Length > 190 Then Return BadRequest(New Erreur("Adresse e-mail invalide."))
        If nom.Length = 0 OrElse nom.Length > 120 Then Return BadRequest(New Erreur("Indique ton nom (120 caractères au plus)."))
        If If(d.MotDePasse, "").Length < 8 Then Return BadRequest(New Erreur("Le mot de passe doit faire au moins 8 caractères."))

        Using cn = Await _base.OuvrirAsync()
            Dim existe = Await cn.ExecuteScalarAsync(Of Integer)("SELECT COUNT(*) FROM enseignant WHERE email = @email", New With {email})
            If existe > 0 Then Return Conflict(New Erreur("Un compte existe déjà avec cette adresse."))
            Dim id = Await cn.ExecuteScalarAsync(Of Integer)(
                "INSERT INTO enseignant (email, nom, mot_de_passe, date_creation) VALUES (@email, @nom, @mdp, @date); " & _base.SqlDernierId,
                New With {email, nom, .mdp = BCrypt.Net.BCrypt.HashPassword(d.MotDePasse), .date = DateTime.UtcNow})
            Return Ok(ReponseEnseignant(id, nom))
        End Using
    End Function

    <HttpPost("enseignant/connexion")>
    <EnableRateLimiting("connexion")>
    Public Async Function ConnexionEnseignant(d As ConnexionEnseignant) As Task(Of IActionResult)
        Dim email = If(d.Email, "").Trim().ToLowerInvariant()
        Using cn = Await _base.OuvrirAsync()
            Dim ens = Await cn.QueryFirstOrDefaultAsync(Of EnseignantLigne)(
                "SELECT id, email, nom, mot_de_passe FROM enseignant WHERE email = @email", New With {email})
            If ens Is Nothing OrElse Not BCrypt.Net.BCrypt.Verify(If(d.MotDePasse, ""), ens.MotDePasse) Then
                Return Unauthorized(New Erreur("E-mail ou mot de passe incorrect."))
            End If
            Return Ok(ReponseEnseignant(ens.Id, ens.Nom))
        End Using
    End Function

    ''' <summary>Connexion d'un élève : code de la classe + identifiant + code PIN à 4 chiffres.</summary>
    <HttpPost("eleve/connexion")>
    <EnableRateLimiting("connexion")>
    Public Async Function ConnexionEleve(d As ConnexionEleve) As Task(Of IActionResult)
        Dim code = If(d.CodeClasse, "").Trim().ToUpperInvariant()
        Dim identifiant = If(d.Identifiant, "").Trim().ToLowerInvariant()
        Dim maintenant = DateTime.UtcNow
        Const refus As String = "Code de classe, identifiant ou code PIN incorrect."

        Using cn = Await _base.OuvrirAsync()
            Dim el = Await cn.QueryFirstOrDefaultAsync(Of EleveLigne)(
                "SELECT e.id, e.classe_id, e.nom, e.prenom, e.identifiant, e.code_pin, e.echecs_connexion, e.bloque_jusqu_a, c.nom AS classe_nom " &
                "FROM eleve e INNER JOIN classe c ON c.id = e.classe_id WHERE c.code = @code AND e.identifiant = @identifiant",
                New With {code, identifiant})
            If el Is Nothing Then Return Unauthorized(New Erreur(refus))

            If el.BloqueJusquA.HasValue AndAlso el.BloqueJusquA.Value > maintenant Then
                Dim minutes = CInt(Math.Ceiling((el.BloqueJusquA.Value - maintenant).TotalMinutes))
                Return StatusCode(423, New Erreur($"Trop d'essais. Réessaie dans {minutes} minute{If(minutes > 1, "s", "")} ou demande un nouveau code à ton professeur."))
            End If

            If Not BCrypt.Net.BCrypt.Verify(If(d.Pin, "").Trim(), el.CodePin) Then
                Dim echecs = el.EchecsConnexion + 1
                Dim bloque As DateTime? = If(echecs >= EchecsAvantBlocage, maintenant.Add(DureeBlocage), CType(Nothing, DateTime?))
                Await cn.ExecuteAsync("UPDATE eleve SET echecs_connexion = @echecs, bloque_jusqu_a = @bloque WHERE id = @id",
                                      New With {.echecs = If(bloque.HasValue, 0, echecs), bloque, el.Id})
                Return Unauthorized(New Erreur(refus))
            End If

            Await cn.ExecuteAsync("UPDATE eleve SET echecs_connexion = 0, bloque_jusqu_a = NULL, derniere_activite = @maintenant WHERE id = @id",
                                  New With {maintenant, el.Id})
            ' Jeton valable 60 jours : l'élève reste connecté sur sa tablette
            Dim jeton = _jetons.Creer(el.Id, Jetons.RoleEleve, TimeSpan.FromDays(60), New Claim("classe", el.ClasseId.ToString()))
            Return Ok(New ReponseConnexion With {
                .Jeton = jeton, .Role = Jetons.RoleEleve, .Nom = el.Nom, .Prenom = el.Prenom, .Classe = el.ClasseNom})
        End Using
    End Function

    ''' <summary>Vérifie un jeton et renvoie le rôle de la personne connectée.</summary>
    <HttpGet("moi")>
    <Authorize>
    Public Function Moi() As IActionResult
        Return Ok(New With {.id = User.FindFirst("sub")?.Value, .role = User.FindFirst("role")?.Value})
    End Function

    Private Function ReponseEnseignant(id As Integer, nom As String) As ReponseConnexion
        Return New ReponseConnexion With {
            .Jeton = _jetons.Creer(id, Jetons.RoleEnseignant, TimeSpan.FromHours(12)),
            .Role = Jetons.RoleEnseignant, .Nom = nom}
    End Function

End Class
