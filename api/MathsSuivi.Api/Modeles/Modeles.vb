' Objets échangés en JSON avec l'application (noms en camelCase côté JSON).

' ---------- Connexion ----------
Public Class InscriptionEnseignant
    Public Property Email As String
    Public Property Nom As String
    Public Property MotDePasse As String
    Public Property CodeInvitation As String
End Class

Public Class ConnexionEnseignant
    Public Property Email As String
    Public Property MotDePasse As String
End Class

Public Class ConnexionEleve
    Public Property CodeClasse As String
    Public Property Identifiant As String
    Public Property Pin As String
End Class

Public Class ReponseConnexion
    Public Property Jeton As String
    Public Property Role As String
    Public Property Nom As String
    Public Property Prenom As String
    Public Property Classe As String
End Class

' ---------- Lignes lues en base ----------
Public Class EnseignantLigne
    Public Property Id As Integer
    Public Property Email As String
    Public Property Nom As String
    Public Property MotDePasse As String
End Class

Public Class EleveLigne
    Public Property Id As Integer
    Public Property ClasseId As Integer
    Public Property Nom As String
    Public Property Prenom As String
    Public Property Identifiant As String
    Public Property CodePin As String
    Public Property EchecsConnexion As Integer
    Public Property BloqueJusquA As DateTime?
    Public Property DerniereActivite As DateTime?
    Public Property ClasseNom As String
End Class

Public Class ChapitreLigne
    Public Property Id As String
    Public Property Titre As String
    Public Property Ordre As Integer
    Public Property NbQcm As Integer
    Public Property NbExercices As Integer
    Public Property NbProblemes As Integer
    Public ReadOnly Property Total As Integer
        Get
            Return NbQcm + NbExercices + NbProblemes
        End Get
    End Property
End Class

' ---------- Classes et élèves ----------
Public Class ClasseInfo
    Public Property Id As Integer
    Public Property Nom As String
    Public Property Code As String
    Public Property NbEleves As Integer
    Public Property DateCreation As DateTime
End Class

Public Class NouvelleClasse
    Public Property Nom As String
End Class

Public Class NouvelEleve
    Public Property Nom As String
    Public Property Prenom As String
End Class

Public Class AjoutEleves
    Public Property Eleves As List(Of NouvelEleve)
End Class

''' <summary>Renvoyé une seule fois à la création : le code PIN n'est stocké que haché.</summary>
Public Class EleveCree
    Public Property Id As Integer
    Public Property Nom As String
    Public Property Prenom As String
    Public Property Identifiant As String
    Public Property Pin As String
End Class

Public Class EleveInfo
    Public Property Id As Integer
    Public Property Nom As String
    Public Property Prenom As String
    Public Property Identifiant As String
    Public Property DerniereActivite As DateTime?
    Public Property Bloque As Boolean
End Class

' ---------- Résultats ----------
''' <summary>Résultat d'une question, tel que stocké en base.</summary>
Public Class ResultatLigne
    Public Property EleveId As Integer
    Public Property Cle As String
    Public Property ChapitreId As String
    Public Property Section As String
    Public Property Reussi As Boolean
    Public Property EssaisRates As Integer
    Public Property Indice As Boolean
    Public Property CorrectionVue As Boolean
    Public Property Points As Decimal
    Public Property DateMaj As DateTime
End Class

''' <summary>Résultat d'une question envoyé par l'application de l'élève.</summary>
Public Class EntreeProgression
    Public Property Cle As String
    Public Property Reussi As Boolean
    Public Property EssaisRates As Integer
    Public Property Indice As Boolean
    Public Property CorrectionVue As Boolean
    Public Property Points As Decimal?
End Class

Public Class EnvoiProgression
    Public Property Entrees As List(Of EntreeProgression)
End Class

Public Class ReponseProgression
    Public Property Resultats As List(Of ResultatLigne)
    Public Property Ignorees As Integer
End Class

' ---------- Bilan de classe ----------
Public Class BilanChapitre
    Public Property Id As String
    Public Property Titre As String
    Public Property Ordre As Integer
    Public Property Total As Integer
End Class

Public Class BilanEleveChapitre
    Public Property ChapitreId As String
    Public Property Points As Decimal
    Public Property Note20 As Decimal?
End Class

Public Class BilanEleve
    Public Property Id As Integer
    Public Property Nom As String
    Public Property Prenom As String
    Public Property Identifiant As String
    Public Property DerniereActivite As DateTime?
    Public Property Points As Decimal
    Public Property Total As Integer
    Public Property Note20 As Decimal
    Public Property Indices As Integer
    Public Property CorrectionsVues As Integer
    Public Property Chapitres As List(Of BilanEleveChapitre)
End Class

Public Class BilanClasse
    Public Property Classe As ClasseInfo
    Public Property Chapitres As List(Of BilanChapitre)
    Public Property Eleves As List(Of BilanEleve)
    Public Property MoyenneClasse As Decimal?
End Class

Public Class Erreur
    Public Property Erreur As String
    Public Sub New(message As String)
        Erreur = message
    End Sub
End Class
