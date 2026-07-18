#!/usr/bin/env bash
set -eu

manual=false
no_start=false
reset_data=false

usage() {
    cat <<'EOF'
Usage: bash scripts/setup-local.sh [--manual] [--no-start] [--reset-data]

  --manual      Interactively enter PostgreSQL and MinIO passwords.
  --no-start    Create or validate .env without starting Docker services.
  --reset-data  Delete this project's Docker containers and volumes before startup.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --manual)
            manual=true
            ;;
        --no-start)
            no_start=true
            ;;
        --reset-data)
            reset_data=true
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
    shift
done

if [ "$no_start" = true ] && [ "$reset_data" = true ]; then
    echo '--no-start and --reset-data cannot be used together.' >&2
    exit 2
fi

repository_root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
env_path="$repository_root/.env"
postgres_volume_name='carbon-footprint_postgres-data'

cd "$repository_root"

if ! command -v docker >/dev/null 2>&1; then
    echo 'Docker was not found. Install Docker Desktop or Docker Engine first.' >&2
    exit 1
fi

if ! docker info >/dev/null 2>&1; then
    echo 'Docker is not running. Start Docker Desktop or Docker Engine first.' >&2
    exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
    echo 'Docker Compose was not found. Confirm that docker compose works.' >&2
    exit 1
fi

new_password() {
    od -An -N24 -tx1 /dev/urandom | tr -d ' \n'
}

validate_manual_password() {
    value="$1"
    case "$value" in
        ''|*[!A-Za-z0-9._-]*)
            return 1
            ;;
    esac

    length=${#value}
    [ "$length" -ge 16 ] && [ "$length" -le 128 ]
}

read_password() {
    label="$1"
    while :; do
        printf '%s (input is hidden): ' "$label" >&2
        IFS= read -r -s first
        printf '\nEnter %s again: ' "$label" >&2
        IFS= read -r -s second
        printf '\n' >&2

        if [ "$first" != "$second" ]; then
            echo 'The two values do not match. Try again.' >&2
            continue
        fi

        if ! validate_manual_password "$first"; then
            echo 'Password must be 16-128 characters and use only letters, numbers, dot, underscore, or hyphen.' >&2
            continue
        fi

        printf '%s' "$first"
        return
    done
}

get_env_value() {
    key="$1"
    awk -v key="$key" '
        index($0, key "=") == 1 {
            sub(/^[^=]*=/, "")
            value = $0
        }
        END { print value }
    ' "$env_path"
}

has_existing_postgres_volume=false
if docker volume inspect "$postgres_volume_name" >/dev/null 2>&1; then
    has_existing_postgres_volume=true
fi

created_env=false
if [ ! -f "$env_path" ]; then
    if [ "$has_existing_postgres_volume" = true ] && [ "$reset_data" = false ]; then
        cat >&2 <<'EOF'
An existing PostgreSQL volume was found, but .env is missing.
Generating a new password would not unlock the existing database.
Restore the original .env, or delete disposable local data with:
  bash scripts/setup-local.sh --reset-data
EOF
        exit 1
    fi

    if [ "$manual" = true ]; then
        postgres_password="$(read_password 'PostgreSQL password')"
        minio_password="$(read_password 'MinIO password')"
    else
        postgres_password="$(new_password)"
        minio_password="$(new_password)"
    fi

    if [ "$postgres_password" = "$minio_password" ]; then
        echo 'PostgreSQL and MinIO must use different passwords.' >&2
        exit 1
    fi

    umask 077
    cat > "$env_path" <<EOF
ASPNETCORE_ENVIRONMENT=Development
POSTGRES_DB=carbon_footprint
POSTGRES_USER=carbon_app
POSTGRES_PASSWORD=$postgres_password
MINIO_ROOT_USER=carbon_minio
MINIO_ROOT_PASSWORD=$minio_password
OBJECTSTORAGE__ENDPOINT=http://minio:9000
OBJECTSTORAGE__BUCKET=carbon-evidence
MAIL__HOST=mailpit
MAIL__PORT=1025
EOF
    chmod 600 "$env_path" 2>/dev/null || true
    created_env=true

    cat <<EOF

Created .env with these local credentials:
  PostgreSQL user: carbon_app
  PostgreSQL password: $postgres_password
  MinIO user: carbon_minio
  MinIO password: $minio_password
  Settings file: $env_path

Keep .env private. Do not commit it, paste it into chat, or share it.
EOF
else
    echo "Found existing .env. Existing passwords will be preserved: $env_path"
fi

postgres_password="$(get_env_value POSTGRES_PASSWORD)"
minio_password="$(get_env_value MINIO_ROOT_PASSWORD)"

case "$postgres_password" in
    ''|*change-this*|*replace-with*)
        echo 'POSTGRES_PASSWORD is not configured. Edit .env, or remove an unused .env and rerun this script.' >&2
        exit 1
        ;;
esac

case "$minio_password" in
    ''|*change-this*|*replace-with*)
        echo 'MINIO_ROOT_PASSWORD is not configured. Edit .env, or remove an unused .env and rerun this script.' >&2
        exit 1
        ;;
esac

if [ "$postgres_password" = "$minio_password" ]; then
    echo 'POSTGRES_PASSWORD and MINIO_ROOT_PASSWORD must be different.' >&2
    exit 1
fi

if [ "$no_start" = true ]; then
    echo 'Configuration is ready. Docker services were not started because --no-start was specified.'
    exit 0
fi

if [ "$reset_data" = true ]; then
    echo 'WARNING: deleting this project’s Docker containers and volume data.' >&2
    docker compose down -v --remove-orphans
fi

docker compose config --quiet
docker compose up -d --build
docker compose ps -a

cat <<'EOF'

Startup command completed. ClamAV can take longer on the first run.
  Application: http://127.0.0.1:8088
  Mailpit:     http://127.0.0.1:8025
  MinIO:       http://127.0.0.1:9001
  Status:      docker compose ps -a
  Logs:        docker compose logs --tail=200

migrate showing Exited (0) means the database migration completed successfully.
EOF

if [ "$created_env" = false ]; then
    echo 'Existing passwords remain available in the local .env file.'
fi
