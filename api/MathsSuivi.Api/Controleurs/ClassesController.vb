Imports System.Data.Common
Imports System.Globalization
Imports System.Text
Imports Dapper
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

''' <summary>Espace enseignant : classes, élèves et bilans. Un enseignant ne voit que ses propres classes.</summary>
<ApiController>
<Route("api/classes")>
<Authorize(Roles:=Jetons.RoleEnseignant)>
Public Class ClassesController
    Inherits ControllerBase

    Private ReadOnly _base As Base

    Public Sub New(base As Base)
        _base = base
    End Sub

    Private ReadOnly Property EnseignantId As Integer
        Get
            Return Integer.Parse(User.FindFirst("sub").Value)
        End Get
    End Property

    ' ---------------------------------------------------------------- Classes

    <HttpGet>
    Public Async Function Lister() As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            Dim classes = Await cn.QueryAsync(Of ClasseInfo)(
                "SELECT c.id, c.nom, c.code, c.date_creation, (SELECT COUNT(*) FROM eleve e WHERE e.classe_id = c.id) AS nb_eleves " &
                "FROM classe c WHERE c.enseignant_id = @ens ORDER BY c.nom", New With {.ens = EnseignantId})
            Return Ok(classes)
        End Using
    End Function

    <HttpPost>
    Public Async Function Creer(d As NouvelleClasse) As Task(Of IActionResult)
        Dim nom = If(d?.Nom, "").Trim()
        If nom.Length = 0 OrElse nom.Length > 80 Then Return BadRequest(New Erreur("Donne un nom à la classe (80 caractères au plus)."))

        Using cn = Await _base.OuvrirAsync()
            ' Code unique : on retire au sort jusqu'à en trouver un libre
            Dim code As String
            Do
                code = Jetons.NouveauCodeClasse()
            Loop While Await cn.ExecuteScalarAsync(Of Integer)("SELECT COUNT(*) FROM classe WHERE code = @code", New With {code}) > 0

            Dim maintenant = DateTime.UtcNow
            Dim id = Await cn.ExecuteScalarAsync(Of Integer)(
                "INSERT INTO classe (enseignant_id, nom, code, date_creation) VALUES (@ens, @nom, @code, @maintenant); " & _base.SqlDernierId,
                New With {.ens = EnseignantId, nom, code, maintenant})
            Return Ok(New ClasseInfo With {.Id = id, .Nom = nom, .Code = code, .NbEleves = 0, .DateCreation = maintenant})
        End Using
    End Function

    <HttpDelete("{id:int}")>
    Public Async Function Supprimer(id As Integer) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            Await SupprimerResultats(cn, "eleve_id IN (SELECT id FROM eleve WHERE classe_id = @id)", New With {id})
            Await cn.ExecuteAsync("DELETE FROM eleve WHERE classe_id = @id", New With {id})
            Await cn.ExecuteAsync("DELETE FROM classe WHERE id = @id", New With {id})
            Return NoContent()
        End Using
    End Function

    ' ---------------------------------------------------------------- Élèves

    <HttpGet("{id:int}/eleves")>
    Public Async Function Eleves(id As Integer) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            Dim lignes = Await cn.QueryAsync(Of EleveLigne)(
                "SELECT id, nom, prenom, identifiant, derniere_activite, bloque_jusqu_a FROM eleve WHERE classe_id = @id ORDER BY nom, prenom",
                New With {id})
            Dim maintenant = DateTime.UtcNow
            Return Ok(lignes.Select(Function(e) New EleveInfo With {
                .Id = e.Id, .Nom = e.Nom, .Prenom = e.Prenom, .Identifiant = e.Identifiant,
                .DerniereActivite = e.DerniereActivite,
                .Bloque = e.BloqueJusquA.HasValue AndAlso e.BloqueJusquA.Value > maintenant}))
        End Using
    End Function

    ''' <summary>Ajoute des élèves. Les codes PIN sont renvoyés en clair une seule fois : à noter ou imprimer.</summary>
    <HttpPost("{id:int}/eleves")>
    Public Async Function AjouterEleves(id As Integer, d As AjoutEleves) As Task(Of IActionResult)
        If d?.Eleves Is Nothing OrElse d.Eleves.Count = 0 Then Return BadRequest(New Erreur("Aucun élève à ajouter."))
        If d.Eleves.Count > 60 Then Return BadRequest(New Erreur("60 élèves au plus à la fois."))
        For Each e In d.Eleves
            If String.IsNullOrWhiteSpace(e.Prenom) OrElse String.IsNullOrWhiteSpace(e.Nom) Then
                Return BadRequest(New Erreur("Chaque élève doit avoir un nom et un prénom."))
            End If
            If e.Prenom.Trim().Length > 80 OrElse e.Nom.Trim().Length > 80 Then
                Return BadRequest(New Erreur("Nom ou prénom trop long (80 caractères au plus)."))
            End If
        Next

        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            Dim pris = (Await cn.QueryAsync(Of String)("SELECT identifiant FROM eleve WHERE classe_id = @id", New With {id})).ToHashSet()
            Dim crees As New List(Of EleveCree)
            Dim maintenant = DateTime.UtcNow

            Using tr = Await cn.BeginTransactionAsync()
                For Each e In d.Eleves
                    Dim prenom = e.Prenom.Trim(), nom = e.Nom.Trim()
                    Dim identifiant = IdentifiantLibre(prenom, nom, pris)
                    pris.Add(identifiant)
                    Dim pin = Jetons.NouveauPin()
                    Dim eleveId = Await cn.ExecuteScalarAsync(Of Integer)(
                        "INSERT INTO eleve (classe_id, nom, prenom, identifiant, code_pin, echecs_connexion, date_creation) " &
                        "VALUES (@id, @nom, @prenom, @identifiant, @pin, 0, @maintenant); " & _base.SqlDernierId,
                        New With {id, nom, prenom, identifiant, .pin = BCrypt.Net.BCrypt.HashPassword(pin), maintenant}, tr)
                    crees.Add(New EleveCree With {.Id = eleveId, .Nom = nom, .Prenom = prenom, .Identifiant = identifiant, .Pin = pin})
                Next
                Await tr.CommitAsync()
            End Using
            Return Ok(crees)
        End Using
    End Function

    ''' <summary>Donne un nouveau code PIN à un élève (oubli ou compte bloqué).</summary>
    <HttpPost("{id:int}/eleves/{eleveId:int}/pin")>
    Public Async Function NouveauPin(id As Integer, eleveId As Integer) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            Dim pin = Jetons.NouveauPin()
            Dim n = Await cn.ExecuteAsync(
                "UPDATE eleve SET code_pin = @pin, echecs_connexion = 0, bloque_jusqu_a = NULL WHERE id = @eleveId AND classe_id = @id",
                New With {.pin = BCrypt.Net.BCrypt.HashPassword(pin), eleveId, id})
            If n = 0 Then Return NotFound(New Erreur("Élève introuvable."))
            Return Ok(New With {.pin = pin})
        End Using
    End Function

    <HttpDelete("{id:int}/eleves/{eleveId:int}")>
    Public Async Function SupprimerEleve(id As Integer, eleveId As Integer) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            Await SupprimerResultats(cn, "eleve_id IN (SELECT id FROM eleve WHERE id = @eleveId AND classe_id = @id)", New With {eleveId, id})
            Dim n = Await cn.ExecuteAsync("DELETE FROM eleve WHERE id = @eleveId AND classe_id = @id", New With {eleveId, id})
            If n = 0 Then Return NotFound(New Erreur("Élève introuvable."))
            Return NoContent()
        End Using
    End Function

    ''' <summary>Efface les résultats d'un élève (tous, ou un seul chapitre) pour lui permettre de recommencer.</summary>
    <HttpDelete("{id:int}/eleves/{eleveId:int}/resultats")>
    Public Async Function EffacerResultats(id As Integer, eleveId As Integer, <FromQuery> chapitre As String) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            If Not Await EleveDeLaClasse(cn, id, eleveId) Then Return NotFound(New Erreur("Élève introuvable."))
            Dim n As Integer
            If String.IsNullOrWhiteSpace(chapitre) Then
                n = Await cn.ExecuteAsync("DELETE FROM resultat WHERE eleve_id = @eleveId", New With {eleveId})
            Else
                n = Await cn.ExecuteAsync("DELETE FROM resultat WHERE eleve_id = @eleveId AND chapitre_id = @chapitre", New With {eleveId, chapitre})
            End If
            Return Ok(New With {.supprimes = n})
        End Using
    End Function

    ''' <summary>Détail question par question d'un élève.</summary>
    <HttpGet("{id:int}/eleves/{eleveId:int}/resultats")>
    Public Async Function ResultatsEleve(id As Integer, eleveId As Integer) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            If Not Await EstAMoi(cn, id) Then Return NotFound(New Erreur("Classe introuvable."))
            If Not Await EleveDeLaClasse(cn, id, eleveId) Then Return NotFound(New Erreur("Élève introuvable."))
            Dim lignes = Await cn.QueryAsync(Of ResultatLigne)(
                "SELECT eleve_id, cle, chapitre_id, section, reussi, essais_rates, indice, correction_vue, points, date_maj " &
                "FROM resultat WHERE eleve_id = @eleveId ORDER BY date_maj DESC", New With {eleveId})
            Return Ok(lignes)
        End Using
    End Function

    ' ---------------------------------------------------------------- Bilan

    ''' <summary>Bilan de la classe : points et note sur 20 par élève et par chapitre.</summary>
    <HttpGet("{id:int}/bilan")>
    Public Async Function Bilan(id As Integer) As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            Dim classe = Await cn.QueryFirstOrDefaultAsync(Of ClasseInfo)(
                "SELECT c.id, c.nom, c.code, c.date_creation, (SELECT COUNT(*) FROM eleve e WHERE e.classe_id = c.id) AS nb_eleves " &
                "FROM classe c WHERE c.id = @id AND c.enseignant_id = @ens", New With {id, .ens = EnseignantId})
            If classe Is Nothing Then Return NotFound(New Erreur("Classe introuvable."))

            Dim chapitres = (Await cn.QueryAsync(Of ChapitreLigne)(
                "SELECT id, titre, ordre, nb_qcm, nb_exercices, nb_problemes FROM chapitre ORDER BY ordre")).ToList()
            Dim eleves = (Await cn.QueryAsync(Of EleveLigne)(
                "SELECT id, nom, prenom, identifiant, derniere_activite FROM eleve WHERE classe_id = @id ORDER BY nom, prenom",
                New With {id})).ToList()
            Dim resultats = (Await cn.QueryAsync(Of ResultatLigne)(
                "SELECT r.eleve_id, r.cle, r.chapitre_id, r.section, r.reussi, r.essais_rates, r.indice, r.correction_vue, r.points, r.date_maj " &
                "FROM resultat r INNER JOIN eleve e ON e.id = r.eleve_id WHERE e.classe_id = @id", New With {id})).
                ToLookup(Function(r) r.EleveId)

            Dim totalGeneral = chapitres.Sum(Function(c) c.Total)
            Dim bilans As New List(Of BilanEleve)
            For Each el In eleves
                Dim siens = resultats(el.Id).ToList()
                Dim parChapitre = siens.ToLookup(Function(r) r.ChapitreId)
                Dim points = siens.Sum(Function(r) r.Points)
                bilans.Add(New BilanEleve With {
                    .Id = el.Id, .Nom = el.Nom, .Prenom = el.Prenom, .Identifiant = el.Identifiant,
                    .DerniereActivite = el.DerniereActivite,
                    .Points = points, .Total = totalGeneral, .Note20 = Note(points, totalGeneral),
                    .Indices = siens.Where(Function(r) r.Indice).Select(Function(r) Regles.Exercice(r.Cle)).Distinct().Count(),
                    .CorrectionsVues = siens.Where(Function(r) r.CorrectionVue AndAlso Not r.Reussi).
                                             Select(Function(r) Regles.Exercice(r.Cle)).Distinct().Count(),
                    .Chapitres = chapitres.Select(Function(c)
                                                      Dim pts = parChapitre(c.Id).Sum(Function(r) r.Points)
                                                      Return New BilanEleveChapitre With {
                                                          .ChapitreId = c.Id, .Points = pts,
                                                          .Note20 = If(parChapitre(c.Id).Any(), Note(pts, c.Total), CType(Nothing, Decimal?))}
                                                  End Function).ToList()})
            Next

            Dim actifs = bilans.Where(Function(b) b.Points > 0).ToList()
            Return Ok(New BilanClasse With {
                .Classe = classe,
                .Chapitres = chapitres.Select(Function(c) New BilanChapitre With {.Id = c.Id, .Titre = c.Titre, .Ordre = c.Ordre, .Total = c.Total}).ToList(),
                .Eleves = bilans,
                .MoyenneClasse = If(actifs.Count > 0, Math.Round(actifs.Average(Function(b) b.Note20), 1), CType(Nothing, Decimal?))})
        End Using
    End Function

    ' ---------------------------------------------------------------- Outils

    Private Async Function EstAMoi(cn As DbConnection, classeId As Integer) As Task(Of Boolean)
        Return Await cn.ExecuteScalarAsync(Of Integer)(
            "SELECT COUNT(*) FROM classe WHERE id = @classeId AND enseignant_id = @ens",
            New With {classeId, .ens = EnseignantId}) > 0
    End Function

    Private Shared Async Function EleveDeLaClasse(cn As DbConnection, classeId As Integer, eleveId As Integer) As Task(Of Boolean)
        Return Await cn.ExecuteScalarAsync(Of Integer)(
            "SELECT COUNT(*) FROM eleve WHERE id = @eleveId AND classe_id = @classeId", New With {eleveId, classeId}) > 0
    End Function

    Private Shared Async Function SupprimerResultats(cn As DbConnection, condition As String, param As Object) As Task
        Await cn.ExecuteAsync("DELETE FROM resultat WHERE " & condition, param)
    End Function

    Private Shared Function Note(points As Decimal, total As Integer) As Decimal
        If total = 0 Then Return 0D
        Return Math.Round(20D * points / total, 1)
    End Function

    ''' <summary>Identifiant du type « lea.m » (prénom sans accents + initiale du nom), unique dans la classe.</summary>
    Public Shared Function IdentifiantLibre(prenom As String, nom As String, pris As HashSet(Of String)) As String
        Dim base = SansAccents(prenom)
        If base.Length = 0 Then base = "eleve"
        If base.Length > 30 Then base = base.Substring(0, 30)
        Dim initiale = SansAccents(nom)
        Dim candidat = If(initiale.Length > 0, base & "." & initiale(0), base)
        Dim resultat = candidat
        Dim n = 2
        While pris.Contains(resultat)
            resultat = candidat & n.ToString()
            n += 1
        End While
        Return resultat
    End Function

    Private Shared Function SansAccents(texte As String) As String
        Dim sb As New StringBuilder()
        For Each c In texte.Normalize(NormalizationForm.FormD).ToLowerInvariant()
            If CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark Then Continue For
            If c >= "a"c AndAlso c <= "z"c Then sb.Append(c)
        Next
        Return sb.ToString()
    End Function

End Class
