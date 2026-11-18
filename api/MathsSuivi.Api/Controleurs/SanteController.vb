Imports Dapper
Imports Microsoft.AspNetCore.Mvc

''' <summary>Vérifie que l'API répond et que la base est joignable.</summary>
<ApiController>
<Route("api/sante")>
Public Class SanteController
    Inherits ControllerBase

    Private ReadOnly _base As Base

    Public Sub New(base As Base)
        _base = base
    End Sub

    <HttpGet>
    Public Async Function Verifier() As Task(Of IActionResult)
        Try
            Using cn = Await _base.OuvrirAsync()
                Dim nb = Await cn.ExecuteScalarAsync(Of Integer)("SELECT COUNT(*) FROM chapitre")
                Return Ok(New With {.statut = "ok", .baseDeDonnees = If(_base.EstMySql, "MySql", "SqlServer"), .chapitres = nb})
            End Using
        Catch ex As Exception
            Return StatusCode(503, New Erreur("Base de données injoignable."))
        End Try
    End Function

End Class
