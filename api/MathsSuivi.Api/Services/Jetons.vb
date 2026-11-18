Imports System.IdentityModel.Tokens.Jwt
Imports System.Security.Claims
Imports System.Security.Cryptography
Imports System.Text
Imports Microsoft.Extensions.Configuration
Imports Microsoft.IdentityModel.Tokens

''' <summary>Création des jetons de connexion (JWT) et des codes aléatoires.</summary>
Public Class Jetons

    Public Const Emetteur As String = "maths4e-api"
    Public Const Audience As String = "maths4e"
    Public Const RoleEleve As String = "eleve"
    Public Const RoleEnseignant As String = "enseignant"

    Private ReadOnly _cle As SymmetricSecurityKey

    Public Sub New(cfg As IConfiguration)
        _cle = New SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg("Jwt:Cle")))
    End Sub

    Public Function Creer(id As Integer, role As String, duree As TimeSpan, ParamArray autres As Claim()) As String
        Dim revendications As New List(Of Claim) From {
            New Claim("sub", id.ToString()),
            New Claim("role", role)
        }
        revendications.AddRange(autres)
        Dim maintenant = DateTime.UtcNow
        Dim jeton As New JwtSecurityToken(Emetteur, Audience, revendications, maintenant, maintenant.Add(duree),
                                          New SigningCredentials(_cle, SecurityAlgorithms.HmacSha256))
        Return New JwtSecurityTokenHandler().WriteToken(jeton)
    End Function

    ''' <summary>Code PIN élève à 4 chiffres.</summary>
    Public Shared Function NouveauPin() As String
        Return RandomNumberGenerator.GetInt32(0, 10000).ToString("D4")
    End Function

    ''' <summary>Code de classe à 6 caractères, sans caractères ambigus (0/O, 1/I/L).</summary>
    Public Shared Function NouveauCodeClasse() As String
        Const alphabet As String = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
        Dim sb As New StringBuilder(6)
        For i = 1 To 6
            sb.Append(alphabet(RandomNumberGenerator.GetInt32(alphabet.Length)))
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Comparaison en temps constant (évite de deviner un code caractère par caractère).</summary>
    Public Shared Function Egaux(a As String, b As String) As Boolean
        Return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(If(a, "")), Encoding.UTF8.GetBytes(If(b, "")))
    End Function

End Class
