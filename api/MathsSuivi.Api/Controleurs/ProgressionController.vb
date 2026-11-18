Imports Dapper
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

''' <summary>Résultats de l'élève connecté. Toute modification passe par les règles de Regles.vb.</summary>
<ApiController>
<Route("api/progression")>
<Authorize(Roles:=Jetons.RoleEleve)>
Public Class ProgressionController
    Inherits ControllerBase

    Private Const MaxEntrees As Integer = 500
    Private Const ColonnesResultat As String =
        "eleve_id, cle, chapitre_id, section, reussi, essais_rates, indice, correction_vue, points, date_maj"

    Private ReadOnly _base As Base

    Public Sub New(base As Base)
        _base = base
    End Sub

    Private ReadOnly Property EleveId As Integer
        Get
            Return Integer.Parse(User.FindFirst("sub").Value)
        End Get
    End Property

    ''' <summary>Tous les résultats de l'élève (pour retrouver sa progression sur une autre tablette).</summary>
    <HttpGet>
    Public Async Function Lire() As Task(Of IActionResult)
        Using cn = Await _base.OuvrirAsync()
            Dim lignes = Await cn.QueryAsync(Of ResultatLigne)(
                $"SELECT {ColonnesResultat} FROM resultat WHERE eleve_id = @id ORDER BY cle", New With {.id = EleveId})
            Return Ok(New ReponseProgression With {.Resultats = lignes.ToList(), .Ignorees = 0})
        End Using
    End Function

    ''' <summary>Enregistre des résultats. Renvoie l'état final retenu par le serveur.</summary>
    <HttpPost>
    Public Async Function Envoyer(d As EnvoiProgression) As Task(Of IActionResult)
        If d?.Entrees Is Nothing OrElse d.Entrees.Count = 0 Then Return BadRequest(New Erreur("Aucun résultat à enregistrer."))
        If d.Entrees.Count > MaxEntrees Then Return BadRequest(New Erreur($"{MaxEntrees} résultats au plus par envoi."))

        Dim id = EleveId
        Dim maintenant = DateTime.UtcNow
        Dim ignorees = 0

        Using cn = Await _base.OuvrirAsync()
            Dim chapitres = (Await cn.QueryAsync(Of String)("SELECT id FROM chapitre")).ToHashSet()

            Using tr = Await cn.BeginTransactionAsync()
                Dim existants = (Await cn.QueryAsync(Of ResultatLigne)(
                    $"SELECT {ColonnesResultat} FROM resultat WHERE eleve_id = @id", New With {id}, tr)).
                    ToDictionary(Function(r) r.Cle)

                For Each e In d.Entrees
                    Dim chapitre As String = Nothing, section As String = Nothing
                    If Not Regles.Analyser(e.Cle, chapitre, section) OrElse Not chapitres.Contains(chapitre) Then
                        ignorees += 1
                        Continue For
                    End If

                    Dim ancien As ResultatLigne = Nothing
                    existants.TryGetValue(e.Cle, ancien)
                    Dim nouveau = Regles.Fusionner(ancien, e, chapitre, section, maintenant)
                    If nouveau Is Nothing Then
                        If ancien IsNot Nothing AndAlso section = "q" Then ignorees += 1
                        Continue For
                    End If
                    nouveau.EleveId = id

                    If ancien Is Nothing Then
                        Await cn.ExecuteAsync(
                            "INSERT INTO resultat (eleve_id, cle, chapitre_id, section, reussi, essais_rates, indice, correction_vue, points, date_maj) " &
                            "VALUES (@EleveId, @Cle, @ChapitreId, @Section, @Reussi, @EssaisRates, @Indice, @CorrectionVue, @Points, @DateMaj)",
                            nouveau, tr)
                    Else
                        Await cn.ExecuteAsync(
                            "UPDATE resultat SET reussi = @Reussi, essais_rates = @EssaisRates, indice = @Indice, " &
                            "correction_vue = @CorrectionVue, points = @Points, date_maj = @DateMaj WHERE eleve_id = @EleveId AND cle = @Cle",
                            nouveau, tr)
                    End If
                    existants(e.Cle) = nouveau
                Next

                Await cn.ExecuteAsync("UPDATE eleve SET derniere_activite = @maintenant WHERE id = @id", New With {maintenant, id}, tr)
                Await tr.CommitAsync()

                Return Ok(New ReponseProgression With {
                    .Resultats = existants.Values.OrderBy(Function(r) r.Cle).ToList(),
                    .Ignorees = ignorees})
            End Using
        End Using
    End Function

End Class
