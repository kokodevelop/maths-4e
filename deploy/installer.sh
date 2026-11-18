#!/usr/bin/env bash
# =============================================================================
#  Maths 4e — installation ou mise à jour sur un serveur Ubuntu
#  Site + API de suivi + MySQL sur la même machine, publiés en HTTPS par
#  Tailscale Funnel (adresse https://<serveur>.<tailnet>.ts.net).
#
#  Première installation et mises à jour, depuis le dossier du dépôt cloné :
#      git pull
#      sudo ./deploy/installer.sh
#
#  Variables facultatives :
#      FUNNEL_PORT=8443   port public Funnel (443, 8443 ou 10000 ; 443 par défaut)
#      PORT_LOCAL=8090    port local du service (127.0.0.1 seulement). Par défaut, le
#                         script prend le premier port libre à partir de 8080 et le
#                         retient dans /etc/maths4e/port pour les mises à jour.
# =============================================================================
set -euo pipefail

DEPOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RACINE=/opt/maths4e
CONF_DIR=/etc/maths4e
CONF="$CONF_DIR/appsettings.Production.json"
SAUVEGARDES=/var/backups/maths4e
SERVICE=maths4e
UTILISATEUR=maths4e
PORT_FICHIER="$CONF_DIR/port"
FUNNEL_PORT="${FUNNEL_PORT:-443}"

etape()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
ok()     { printf '    \033[32m✓\033[0m %s\n' "$*"; }
info()   { printf '    %s\n' "$*"; }
erreur() { printf '\n\033[1;31mErreur : %s\033[0m\n' "$*" >&2; exit 1; }

# ----------------------------------------------------------------- Vérifications
[[ $EUID -eq 0 ]] || erreur "lance le script avec sudo : sudo ./deploy/installer.sh"
[[ -f "$DEPOT/api/MathsSuivi.Api/MathsSuivi.Api.vbproj" && -f "$DEPOT/index.html" ]] \
  || erreur "lance le script depuis le dépôt maths-4e (dossier deploy/)."
# shellcheck source=/dev/null
. /etc/os-release
[[ "${ID:-}" == "ubuntu" ]] || erreur "ce script est prévu pour Ubuntu (système détecté : ${PRETTY_NAME:-inconnu})."
[[ "${VERSION_ID%%.*}" -ge 22 ]] || erreur "Ubuntu 22.04 ou plus récent est nécessaire (.NET 8)."
command -v systemctl >/dev/null || erreur "systemd est nécessaire."
command -v tailscale >/dev/null || erreur "Tailscale n'est pas installé sur ce serveur."
case "$FUNNEL_PORT" in 443|8443|10000) ;; *) erreur "FUNNEL_PORT doit valoir 443, 8443 ou 10000." ;; esac

# Version de Tailscale : la syntaxe « tailscale funnel --bg » existe depuis la 1.52
TS_VERSION="$(tailscale version | head -n1)"
python3 - "$TS_VERSION" <<'PY' || erreur "Tailscale $TS_VERSION est trop ancien : mets-le à jour (1.52 ou plus récent)."
import sys
maj, mineur = (int(x) for x in sys.argv[1].split(".")[:2])
sys.exit(0 if (maj, mineur) >= (1, 52) else 1)
PY
TS_DNS="$(tailscale status --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')" \
  || erreur "Tailscale n'est pas connecté (lance « sudo tailscale up »)."
[[ -n "$TS_DNS" ]] || erreur "nom DNS Tailscale introuvable : active MagicDNS et HTTPS dans la console Tailscale."
URL_PUBLIQUE="https://$TS_DNS$([[ $FUNNEL_PORT == 443 ]] || echo ":$FUNNEL_PORT")"

# Port local du service : choisi par l'utilisateur, sinon celui retenu à l'installation
# précédente, sinon le premier port libre (les conteneurs Docker en prennent souvent).
port_pris() {
  if command -v ss >/dev/null; then [[ -n "$(ss -ltnH "sport = :$1")" ]]
  else (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
  fi
}
SERVICE_ACTIF=0
systemctl is-active --quiet "$SERVICE" && SERVICE_ACTIF=1
if [[ -n "${PORT_LOCAL:-}" ]]; then
  [[ "$PORT_LOCAL" =~ ^[0-9]+$ && "$PORT_LOCAL" -ge 1024 && "$PORT_LOCAL" -le 65535 ]] || erreur "PORT_LOCAL doit être un nombre entre 1024 et 65535."
elif [[ -f "$PORT_FICHIER" ]]; then
  PORT_LOCAL="$(tr -dc '0-9' < "$PORT_FICHIER")"
else
  for p in 8080 $(seq 8090 8099) $(seq 18080 18099); do
    if ! port_pris "$p"; then PORT_LOCAL=$p; break; fi
  done
  [[ -n "${PORT_LOCAL:-}" ]] || erreur "aucun port libre trouvé. Indique-en un : sudo PORT_LOCAL=19080 ./deploy/installer.sh"
fi
# Le port est libre, ou c'est déjà celui de notre service
ANCIEN_PORT="$(sed -n 's/^Environment=ASPNETCORE_URLS=http:\/\/127\.0\.0\.1:\([0-9]*\)$/\1/p' "/etc/systemd/system/$SERVICE.service" 2>/dev/null || true)"
if port_pris "$PORT_LOCAL" && ! [[ $SERVICE_ACTIF == 1 && "$ANCIEN_PORT" == "$PORT_LOCAL" ]]; then
  erreur "le port $PORT_LOCAL est déjà utilisé par un autre programme. Relance avec un port libre : sudo PORT_LOCAL=19080 ./deploy/installer.sh"
fi
install -d -m 750 -o root -g root "$CONF_DIR"
echo "$PORT_LOCAL" > "$PORT_FICHIER"
echo "Port local du service : $PORT_LOCAL"

# Le port Funnel ne doit pas déjà servir une autre application
AUTRE="$(tailscale serve status --json 2>/dev/null | python3 -c '
import json, sys
port, locaux = sys.argv[1], [x for x in sys.argv[2:] if x]
try: cfg = json.load(sys.stdin)
except Exception: sys.exit(0)
for hote, web in (cfg.get("Web") or {}).items():
    if hote.endswith(":" + port):
        for chemin, h in (web.get("Handlers") or {}).items():
            cible = h.get("Proxy") or h.get("Path") or h.get("Text") or "?"
            nos_cibles = [f(l) for l in locaux for f in (lambda l: "http://127.0.0.1:" + l, lambda l: "http://localhost:" + l, lambda l: l)]
            if cible.rstrip("/") not in nos_cibles:
                print(chemin + " -> " + cible)
' "$FUNNEL_PORT" "$PORT_LOCAL" "$ANCIEN_PORT" || true)"
[[ -z "$AUTRE" ]] || erreur "Tailscale publie déjà autre chose sur le port $FUNNEL_PORT ($AUTRE).
  Choisis un autre port public : sudo FUNNEL_PORT=8443 ./deploy/installer.sh"

echo "Maths 4e sera publié sur : $URL_PUBLIQUE"

# ----------------------------------------------------------------- 1. Paquets
etape "1/7 Paquets (.NET 8, MySQL)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
PAQUETS=(dotnet-sdk-8.0 openssl python3 curl gzip)
if dpkg -s mariadb-server >/dev/null 2>&1; then
  info "MariaDB est déjà installé : il sera utilisé à la place de MySQL."
else
  PAQUETS+=(mysql-server)
fi
apt-get install -y -qq "${PAQUETS[@]}" >/dev/null
systemctl enable --now mysql >/dev/null 2>&1 || systemctl enable --now mariadb >/dev/null
ok ".NET $(dotnet --version), $(mysql --version | awk '{print $1, $2, $3}')"

# ----------------------------------------------------------------- 2. Configuration
etape "2/7 Configuration"
install -d -m 750 -o root -g root "$CONF_DIR"
id "$UTILISATEUR" >/dev/null 2>&1 || useradd --system --home-dir /nonexistent --no-create-home --shell /usr/sbin/nologin "$UTILISATEUR"
PREMIERE_INSTALL=0
if [[ ! -f "$CONF" ]]; then
  PREMIERE_INSTALL=1
  python3 - "$CONF" "$RACINE/site" <<'PY'
import json, secrets, string, sys
chemin, site = sys.argv[1], sys.argv[2]
alpha = string.ascii_letters + string.digits
conf = {
  "Base": {"Fournisseur": "MySql"},
  "ConnectionStrings": {"MySql": "Server=localhost;Port=3306;Database=maths4e;User ID=maths4e;Password="
                        + "".join(secrets.choice(alpha) for _ in range(32)) + ";"},
  "Jwt": {"Cle": secrets.token_urlsafe(48)},
  "Inscription": {"CodeInvitation": "".join(secrets.choice("ABCDEFGHJKMNPQRSTUVWXYZ23456789") for _ in range(8))},
  "Cors": {"Origines": ["https://maths-4e-sigma.vercel.app"]},
  "Site": {"Dossier": site},
  "Securite": {"ConnexionsParMinute": 60}
}
with open(chemin, "w", encoding="utf-8") as f:
    json.dump(conf, f, indent=2, ensure_ascii=False)
PY
  ok "secrets générés dans $CONF"
else
  ok "configuration existante conservée ($CONF)"
fi
chown root:"$UTILISATEUR" "$CONF"
chmod 640 "$CONF"
# lire_conf Section.Cle : lit une valeur de la configuration JSON
lire_conf() {
  python3 -c 'import json,sys
v=json.load(open(sys.argv[1], encoding="utf-8"))
for k in sys.argv[2].split("."): v=v[k]
print(v)' "$CONF" "$1"
}
MDP_BASE="$(lire_conf ConnectionStrings.MySql | sed -n 's/.*Password=\([^;]*\);.*/\1/p')"
CODE_INVITATION="$(lire_conf Inscription.CodeInvitation)"
[[ "$MDP_BASE" =~ ^[A-Za-z0-9]+$ ]] || erreur "le mot de passe MySQL de $CONF doit contenir uniquement des lettres et des chiffres."

# ----------------------------------------------------------------- 3. Base de données
etape "3/7 Base de données"
mysql < "$DEPOT/api/sql/01_schema_mysql.sql"
mysql <<SQL
CREATE USER IF NOT EXISTS 'maths4e'@'localhost' IDENTIFIED BY '${MDP_BASE}';
ALTER USER 'maths4e'@'localhost' IDENTIFIED BY '${MDP_BASE}';
GRANT SELECT, INSERT, UPDATE, DELETE ON maths4e.* TO 'maths4e'@'localhost';
FLUSH PRIVILEGES;
SQL
ok "base maths4e prête ($(mysql -N maths4e -e 'SELECT COUNT(*) FROM chapitre') chapitres, $(mysql -N maths4e -e 'SELECT COUNT(*) FROM eleve') élèves)"

# ----------------------------------------------------------------- 4. Compilation
etape "4/7 Compilation de l'API"
CONSTRUCTION="$(mktemp -d)"
trap 'rm -rf "$CONSTRUCTION"' EXIT
# Compile avec le compte qui a lancé sudo (pas de fichiers root dans le dépôt)
CONSTRUCTEUR="${SUDO_USER:-root}"
chown "$CONSTRUCTEUR" "$CONSTRUCTION"
sudo -u "$CONSTRUCTEUR" -H env DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
  dotnet publish "$DEPOT/api/MathsSuivi.Api" -c Release -o "$CONSTRUCTION/api" --nologo -v q \
  || erreur "la compilation a échoué (voir les messages ci-dessus)."
ok "API compilée"

# ----------------------------------------------------------------- 5. Installation des fichiers
etape "5/7 Installation des fichiers dans $RACINE"
install -d -m 755 "$RACINE"
rm -rf "$RACINE/api.new" "$RACINE/site.new"
cp -r "$CONSTRUCTION/api" "$RACINE/api.new"
install -m 640 -o root -g "$UTILISATEUR" "$CONF" "$RACINE/api.new/appsettings.Production.json"
mkdir -p "$RACINE/site.new"
cp "$DEPOT"/{index.html,prof.html,sw.js,manifest.webmanifest} "$RACINE/site.new/"
cp -r "$DEPOT/icons" "$RACINE/site.new/"
# Sur ce serveur, l'API est à la même adresse que le site
cat > "$RACINE/site.new/config.js" <<'JS'
/* Généré par deploy/installer.sh : le site et l'API sont servis à la même adresse. */
window.MATHS4E_API = location.origin;
JS
chown -R root:root "$RACINE/api.new" "$RACINE/site.new"
chgrp "$UTILISATEUR" "$RACINE/api.new/appsettings.Production.json"
chmod -R o+rX "$RACINE/site.new"

systemctl stop "$SERVICE" 2>/dev/null || true
for d in api site; do
  rm -rf "$RACINE/$d.old"
  [[ -d "$RACINE/$d" ]] && mv "$RACINE/$d" "$RACINE/$d.old"
  mv "$RACINE/$d.new" "$RACINE/$d"
done
ok "fichiers installés (version précédente gardée dans api.old et site.old)"

# ----------------------------------------------------------------- 6. Service systemd
etape "6/7 Service $SERVICE"
cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=Maths 4e (site et API de suivi des élèves)
After=network-online.target mysql.service mariadb.service
Wants=network-online.target

[Service]
User=$UTILISATEUR
Group=$UTILISATEUR
WorkingDirectory=$RACINE/api
ExecStart=$(command -v dotnet) $RACINE/api/MathsSuivi.Api.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:$PORT_LOCAL
Environment=DOTNET_NOLOGO=1
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Restart=always
RestartSec=5
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
PrivateDevices=true

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable --now "$SERVICE" >/dev/null 2>&1
systemctl restart "$SERVICE"

for _ in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:$PORT_LOCAL/api/sante" >/dev/null 2>&1; then break; fi
  sleep 1
done
if ! curl -fsS "http://127.0.0.1:$PORT_LOCAL/api/sante" >/dev/null 2>&1; then
  journalctl -u "$SERVICE" -n 30 --no-pager || true
  erreur "le service ne répond pas. Pour revenir à la version précédente : sudo $DEPOT/deploy/restaurer.sh"
fi
PAGE="$(curl -fsS "http://127.0.0.1:$PORT_LOCAL/" || true)"
[[ "$PAGE" == *"<title>Maths 4e</title>"* ]] || erreur "le site n'est pas servi par le service."
ok "service actif : $(curl -fsS "http://127.0.0.1:$PORT_LOCAL/api/sante")"

# ----------------------------------------------------------------- 7. Tailscale Funnel et sauvegardes
etape "7/7 Publication Tailscale Funnel et sauvegardes"
tailscale funnel --bg --yes --https="$FUNNEL_PORT" "http://127.0.0.1:$PORT_LOCAL" >/dev/null 2>&1 \
  || tailscale funnel --bg --https="$FUNNEL_PORT" "http://127.0.0.1:$PORT_LOCAL" >/dev/null \
  || erreur "Funnel n'a pas pu être activé. Vérifie que Funnel est autorisé pour ce serveur dans la console Tailscale (Access controls → nodeAttrs « funnel »)."
ok "publié sur $URL_PUBLIQUE"

install -d -m 750 "$SAUVEGARDES"
cat > /usr/local/sbin/maths4e-sauvegarde <<'SH'
#!/usr/bin/env bash
# Sauvegarde quotidienne de la base maths4e (14 jours conservés)
set -euo pipefail
dossier=/var/backups/maths4e
mysqldump --single-transaction --routines maths4e | gzip > "$dossier/maths4e-$(date +%F).sql.gz"
find "$dossier" -name 'maths4e-*.sql.gz' -mtime +14 -delete
SH
chmod 750 /usr/local/sbin/maths4e-sauvegarde
echo "17 2 * * * root /usr/local/sbin/maths4e-sauvegarde" > /etc/cron.d/maths4e-sauvegarde
/usr/local/sbin/maths4e-sauvegarde
ok "sauvegarde chaque nuit à 2 h 17 dans $SAUVEGARDES"

# ----------------------------------------------------------------- Résumé
cat <<FIN

$(printf '\033[1;32m')Maths 4e est en ligne.$(printf '\033[0m')

  Élèves (à installer sur les tablettes) : $URL_PUBLIQUE/
  Espace enseignant                      : $URL_PUBLIQUE/prof.html
  Code d'invitation enseignant           : $CODE_INVITATION

FIN
if [[ $PREMIERE_INSTALL == 1 ]]; then
  echo "  Crée ton compte enseignant sur la page prof.html avec ce code d'invitation."
  echo "  Le code est aussi dans $CONF (Inscription:CodeInvitation)."
  echo
fi
cat <<FIN
  Journal du service : journalctl -u $SERVICE -f
  Mettre à jour      : git pull && sudo ./deploy/installer.sh
  Revenir en arrière : sudo ./deploy/restaurer.sh
FIN
