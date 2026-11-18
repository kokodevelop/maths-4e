Imports System.Data.Common
Imports Microsoft.Data.SqlClient
Imports Microsoft.Extensions.Configuration
Imports MySqlConnector

''' <summary>
''' Ouvre les connexions vers MySQL ou SQL Server selon Base:Fournisseur.
''' Les requêtes de l'API sont écrites pour fonctionner sur les deux.
''' </summary>
Public Class Base

    Public ReadOnly Property EstMySql As Boolean
    Private ReadOnly _chaine As String

    Public Sub New(cfg As IConfiguration)
        Dim fournisseur = If(cfg("Base:Fournisseur"), "MySql")
        If fournisseur.Equals("MySql", StringComparison.OrdinalIgnoreCase) Then
            EstMySql = True
        ElseIf fournisseur.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) Then
            EstMySql = False
        Else
            Throw New InvalidOperationException("Base:Fournisseur doit valoir MySql ou SqlServer.")
        End If

        _chaine = cfg.GetConnectionString(If(EstMySql, "MySql", "SqlServer"))
        If String.IsNullOrWhiteSpace(_chaine) Then
            Throw New InvalidOperationException(
                $"Chaîne de connexion ConnectionStrings:{If(EstMySql, "MySql", "SqlServer")} manquante.")
        End If
    End Sub

    Public Async Function OuvrirAsync() As Task(Of DbConnection)
        Dim cn As DbConnection
        If EstMySql Then
            cn = New MySqlConnection(_chaine)
        Else
            cn = New SqlConnection(_chaine)
        End If
        Await cn.OpenAsync()
        Return cn
    End Function

    ''' <summary>Requête à ajouter après un INSERT pour récupérer l'identifiant créé.</summary>
    Public ReadOnly Property SqlDernierId As String
        Get
            Return If(EstMySql, "SELECT LAST_INSERT_ID();", "SELECT CAST(SCOPE_IDENTITY() AS INT);")
        End Get
    End Property

End Class
