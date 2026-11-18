-- =====================================================================
--  Maths 4e : base de suivi des élèves — SQL Server 2016 ou plus récent
--  Exécuter une seule fois dans SSMS (ou sqlcmd -i 01_schema_sqlserver.sql)
-- =====================================================================
IF DB_ID(N'maths4e') IS NULL CREATE DATABASE maths4e;
GO
USE maths4e;
GO

IF OBJECT_ID(N'dbo.enseignant') IS NULL
CREATE TABLE dbo.enseignant (
  id               INT IDENTITY(1,1) PRIMARY KEY,
  email            NVARCHAR(190) NOT NULL CONSTRAINT uq_enseignant_email UNIQUE,
  nom              NVARCHAR(120) NOT NULL,
  mot_de_passe     NVARCHAR(100) NOT NULL,          -- hachage BCrypt
  date_creation    DATETIME2     NOT NULL
);

IF OBJECT_ID(N'dbo.classe') IS NULL
CREATE TABLE dbo.classe (
  id               INT IDENTITY(1,1) PRIMARY KEY,
  enseignant_id    INT           NOT NULL CONSTRAINT fk_classe_enseignant REFERENCES dbo.enseignant(id) ON DELETE CASCADE,
  nom              NVARCHAR(80)  NOT NULL,
  code             CHAR(6)       NOT NULL CONSTRAINT uq_classe_code UNIQUE,
  date_creation    DATETIME2     NOT NULL
);

IF OBJECT_ID(N'dbo.eleve') IS NULL
CREATE TABLE dbo.eleve (
  id               INT IDENTITY(1,1) PRIMARY KEY,
  classe_id        INT           NOT NULL CONSTRAINT fk_eleve_classe REFERENCES dbo.classe(id) ON DELETE CASCADE,
  nom              NVARCHAR(80)  NOT NULL,
  prenom           NVARCHAR(80)  NOT NULL,
  identifiant      NVARCHAR(40)  NOT NULL,
  code_pin         NVARCHAR(100) NOT NULL,          -- hachage BCrypt du code à 4 chiffres
  echecs_connexion INT           NOT NULL CONSTRAINT df_eleve_echecs DEFAULT 0,
  bloque_jusqu_a   DATETIME2     NULL,
  derniere_activite DATETIME2    NULL,
  date_creation    DATETIME2     NOT NULL,
  CONSTRAINT uq_eleve_identifiant UNIQUE (classe_id, identifiant)
);

IF OBJECT_ID(N'dbo.chapitre') IS NULL
CREATE TABLE dbo.chapitre (
  id               NVARCHAR(40)  PRIMARY KEY,
  titre            NVARCHAR(120) NOT NULL,
  ordre            INT           NOT NULL,
  nb_qcm           INT           NOT NULL,
  nb_exercices     INT           NOT NULL,
  nb_problemes     INT           NOT NULL
);

IF OBJECT_ID(N'dbo.resultat') IS NULL
CREATE TABLE dbo.resultat (
  id               INT IDENTITY(1,1) PRIMARY KEY,
  eleve_id         INT           NOT NULL CONSTRAINT fk_resultat_eleve REFERENCES dbo.eleve(id) ON DELETE CASCADE,
  cle              NVARCHAR(80)  NOT NULL,
  chapitre_id      NVARCHAR(40)  NOT NULL CONSTRAINT fk_resultat_chapitre REFERENCES dbo.chapitre(id),
  section          CHAR(1)       NOT NULL,
  reussi           BIT           NOT NULL CONSTRAINT df_resultat_reussi DEFAULT 0,
  essais_rates     INT           NOT NULL CONSTRAINT df_resultat_essais DEFAULT 0,
  indice           BIT           NOT NULL CONSTRAINT df_resultat_indice DEFAULT 0,
  correction_vue   BIT           NOT NULL CONSTRAINT df_resultat_correction DEFAULT 0,
  points           DECIMAL(4,2)  NOT NULL CONSTRAINT df_resultat_points DEFAULT 0,
  date_maj         DATETIME2     NOT NULL,
  CONSTRAINT uq_resultat UNIQUE (eleve_id, cle)
);
GO

-- Chapitres de l'application et nombre de points de chaque partie
MERGE dbo.chapitre AS cible
USING (VALUES
(N'relatifs', N'Nombres relatifs', 1, 4, 6, 4),
(N'fractions', N'Fractions', 2, 4, 6, 5),
(N'puissances', N'Puissances', 3, 4, 7, 7),
(N'arithmetique', N'Nombres premiers', 4, 4, 6, 5),
(N'litteral', N'Calcul littéral', 5, 4, 7, 6),
(N'equations', N'Équations', 6, 4, 6, 5),
(N'proportionnalite', N'Proportionnalité', 7, 4, 6, 6),
(N'statistiques', N'Statistiques', 8, 4, 7, 5),
(N'probabilites', N'Probabilités', 9, 4, 5, 5),
(N'pythagore', N'Théorème de Pythagore', 10, 4, 5, 4),
(N'thales', N'Théorème de Thalès', 11, 4, 5, 3),
(N'transformations', N'Translations et rotations', 12, 4, 9, 5),
(N'solides', N'Pyramides et cônes', 13, 4, 6, 4),
(N'vitesse', N'Vitesse et durées', 14, 4, 5, 5)
) AS src (id, titre, ordre, nb_qcm, nb_exercices, nb_problemes)
ON cible.id = src.id
WHEN MATCHED THEN UPDATE SET titre=src.titre, ordre=src.ordre, nb_qcm=src.nb_qcm, nb_exercices=src.nb_exercices, nb_problemes=src.nb_problemes
WHEN NOT MATCHED THEN INSERT (id, titre, ordre, nb_qcm, nb_exercices, nb_problemes)
  VALUES (src.id, src.titre, src.ordre, src.nb_qcm, src.nb_exercices, src.nb_problemes);
GO
