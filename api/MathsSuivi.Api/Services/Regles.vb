Imports System.Text.RegularExpressions

''' <summary>
''' Règles de contrôle des résultats, appliquées par le serveur.
''' Un résultat ne peut qu'empirer ou être complété, jamais effacé :
''' - QCM : la première réponse enregistrée est définitive ;
''' - exercices et problèmes : les essais ratés, l'indice et la correction vue ne reviennent jamais en arrière ;
''' - une question devient réussie seulement si la correction n'a pas été vue avant et s'il reste un essai ;
''' - 1 point si juste du premier coup sans indice, ½ point sinon.
''' </summary>
Public Module Regles

    Public Const MaxEssais As Integer = 3

    ' chapitre:q:numéro  ou  chapitre:(e|p):exercice:question
    Private ReadOnly FormatCle As New Regex("^([a-z]{2,40}):(q|e|p):(\d{1,2})(?::(\d{1,2}))?$", RegexOptions.CultureInvariant)

    ''' <summary>Vérifie la forme de la clé et renvoie le chapitre et la section.</summary>
    Public Function Analyser(cle As String, ByRef chapitre As String, ByRef section As String) As Boolean
        If String.IsNullOrEmpty(cle) Then Return False
        Dim m = FormatCle.Match(cle)
        If Not m.Success Then Return False
        chapitre = m.Groups(1).Value
        section = m.Groups(2).Value
        Dim aQuestion = m.Groups(4).Success
        ' Un QCM n'a pas de numéro de question, un exercice en a toujours un
        Return If(section = "q", Not aQuestion, aQuestion)
    End Function

    ''' <summary>
    ''' Fusionne ce qu'envoie l'élève avec ce qui est déjà en base.
    ''' Renvoie Nothing si rien ne change (ou si la modification est refusée).
    ''' </summary>
    Public Function Fusionner(ancien As ResultatLigne, envoi As EntreeProgression,
                              chapitre As String, section As String, maintenant As DateTime) As ResultatLigne
        If section = "q" Then
            ' QCM : seule la première réponse compte
            If ancien IsNot Nothing Then Return Nothing
            Return New ResultatLigne With {
                .Cle = envoi.Cle, .ChapitreId = chapitre, .Section = section,
                .Reussi = envoi.Reussi, .EssaisRates = If(envoi.Reussi, 0, 1),
                .Points = If(envoi.Reussi, 1D, 0D), .DateMaj = maintenant}
        End If

        Dim a = If(ancien, New ResultatLigne With {.Cle = envoi.Cle, .ChapitreId = chapitre, .Section = section})
        Dim r As New ResultatLigne With {
            .EleveId = a.EleveId, .Cle = a.Cle, .ChapitreId = chapitre, .Section = section,
            .Reussi = a.Reussi, .Points = a.Points,
            .EssaisRates = Math.Min(MaxEssais, Math.Max(a.EssaisRates, Math.Max(0, envoi.EssaisRates))),
            .Indice = a.Indice OrElse envoi.Indice,
            .CorrectionVue = a.CorrectionVue OrElse envoi.CorrectionVue,
            .DateMaj = maintenant}

        If Not a.Reussi AndAlso envoi.Reussi AndAlso Not a.CorrectionVue AndAlso r.EssaisRates < MaxEssais Then
            ' 1 point possible seulement sans essai raté ni indice déjà enregistré
            Dim plafond = If(r.EssaisRates = 0 AndAlso Not a.Indice, 1D, 0.5D)
            Dim demande = If(envoi.Points.HasValue, envoi.Points.Value, plafond)
            r.Reussi = True
            r.Points = If(demande >= 1D, plafond, 0.5D)
        End If

        If ancien IsNot Nothing AndAlso r.Reussi = a.Reussi AndAlso r.Points = a.Points AndAlso
           r.EssaisRates = a.EssaisRates AndAlso r.Indice = a.Indice AndAlso r.CorrectionVue = a.CorrectionVue Then
            Return Nothing
        End If
        Return r
    End Function

    ''' <summary>Exercice auquel appartient une question : pythagore:e:2:0 → pythagore:e:2</summary>
    Public Function Exercice(cle As String) As String
        Dim i = cle.LastIndexOf(":"c)
        Return If(cle.Split(":"c).Length = 4, cle.Substring(0, i), cle)
    End Function

End Module
