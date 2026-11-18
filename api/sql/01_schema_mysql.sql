-- =====================================================================
--  Maths 4e : base de suivi des élèves — MySQL 8
--  Exécuter une seule fois :  mysql -u root -p < 01_schema_mysql.sql
-- =====================================================================
SET NAMES utf8mb4;
CREATE DATABASE IF NOT EXISTS maths4e CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE maths4e;

CREATE TABLE IF NOT EXISTS enseignant (
  id               INT AUTO_INCREMENT PRIMARY KEY,
  email            VARCHAR(190) NOT NULL UNIQUE,
  nom              VARCHAR(120) NOT NULL,
  mot_de_passe     VARCHAR(100) NOT NULL,          -- hachage BCrypt
  date_creation    DATETIME     NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS classe (
  id               INT AUTO_INCREMENT PRIMARY KEY,
  enseignant_id    INT          NOT NULL,
  nom              VARCHAR(80)  NOT NULL,
  code             CHAR(6)      NOT NULL UNIQUE,   -- code donné aux élèves pour se connecter
  date_creation    DATETIME     NOT NULL,
  CONSTRAINT fk_classe_enseignant FOREIGN KEY (enseignant_id) REFERENCES enseignant(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS eleve (
  id               INT AUTO_INCREMENT PRIMARY KEY,
  classe_id        INT          NOT NULL,
  nom              VARCHAR(80)  NOT NULL,
  prenom           VARCHAR(80)  NOT NULL,
  identifiant      VARCHAR(40)  NOT NULL,          -- ex. lea.m (unique dans la classe)
  code_pin         VARCHAR(100) NOT NULL,          -- hachage BCrypt du code à 4 chiffres
  echecs_connexion INT          NOT NULL DEFAULT 0,
  bloque_jusqu_a   DATETIME     NULL,
  derniere_activite DATETIME    NULL,
  date_creation    DATETIME     NOT NULL,
  CONSTRAINT uq_eleve_identifiant UNIQUE (classe_id, identifiant),
  CONSTRAINT fk_eleve_classe FOREIGN KEY (classe_id) REFERENCES classe(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS chapitre (
  id               VARCHAR(40)  PRIMARY KEY,       -- identifiant utilisé par l'application (ex. pythagore)
  titre            VARCHAR(120) NOT NULL,
  ordre            INT          NOT NULL,
  nb_qcm           INT          NOT NULL,
  nb_exercices     INT          NOT NULL,          -- nombre de questions d'exercices
  nb_problemes     INT          NOT NULL           -- nombre de questions de problèmes
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS resultat (
  id               INT AUTO_INCREMENT PRIMARY KEY,
  eleve_id         INT          NOT NULL,
  cle              VARCHAR(80)  NOT NULL,          -- ex. pythagore:e:2:0 (chapitre:section:exercice:question)
  chapitre_id      VARCHAR(40)  NOT NULL,
  section          CHAR(1)      NOT NULL,          -- q = compréhension, e = exercice, p = problème
  reussi           TINYINT(1)   NOT NULL DEFAULT 0,
  essais_rates     INT          NOT NULL DEFAULT 0,
  indice           TINYINT(1)   NOT NULL DEFAULT 0,
  correction_vue   TINYINT(1)   NOT NULL DEFAULT 0,
  points           DECIMAL(4,2) NOT NULL DEFAULT 0,
  date_maj         DATETIME     NOT NULL,
  CONSTRAINT uq_resultat UNIQUE (eleve_id, cle),
  CONSTRAINT fk_resultat_eleve FOREIGN KEY (eleve_id) REFERENCES eleve(id) ON DELETE CASCADE,
  CONSTRAINT fk_resultat_chapitre FOREIGN KEY (chapitre_id) REFERENCES chapitre(id)
) ENGINE=InnoDB;

-- Chapitres de l'application et nombre de points de chaque partie
INSERT INTO chapitre (id, titre, ordre, nb_qcm, nb_exercices, nb_problemes) VALUES
('relatifs', 'Nombres relatifs', 1, 4, 6, 4),
('fractions', 'Fractions', 2, 4, 6, 5),
('puissances', 'Puissances', 3, 4, 7, 7),
('arithmetique', 'Nombres premiers', 4, 4, 6, 5),
('litteral', 'Calcul littéral', 5, 4, 7, 6),
('equations', 'Équations', 6, 4, 6, 5),
('proportionnalite', 'Proportionnalité', 7, 4, 6, 6),
('statistiques', 'Statistiques', 8, 4, 7, 5),
('probabilites', 'Probabilités', 9, 4, 5, 5),
('pythagore', 'Théorème de Pythagore', 10, 4, 5, 4),
('thales', 'Théorème de Thalès', 11, 4, 5, 3),
('transformations', 'Translations et rotations', 12, 4, 9, 5),
('solides', 'Pyramides et cônes', 13, 4, 6, 4),
('vitesse', 'Vitesse et durées', 14, 4, 5, 5)
ON DUPLICATE KEY UPDATE titre=VALUES(titre), ordre=VALUES(ordre), nb_qcm=VALUES(nb_qcm),
  nb_exercices=VALUES(nb_exercices), nb_problemes=VALUES(nb_problemes);
