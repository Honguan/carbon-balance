import json
import sys


def fail(message):
    raise SystemExit(message)


def published_ports(service):
    return service.get("ports", [])


def require_loopback(compose, profile):
    for service_name, service in compose.get("services", {}).items():
        for port in published_ports(service):
            if port.get("host_ip") not in {"127.0.0.1", "::1"}:
                fail(f"{profile}: {service_name} port {port.get('published')} is not loopback-bound")


with open(sys.argv[1], encoding="utf-8") as source:
    development = json.load(source)
with open(sys.argv[2], encoding="utf-8") as source:
    production = json.load(source)

require_loopback(development, "development")
require_loopback(production, "production")

production_services = production.get("services", {})
unexpected = set(production_services) - {"migrate", "web"}
if unexpected:
    fail(f"production: infrastructure services must not be published: {sorted(unexpected)}")

for service_name in ("migrate", "web"):
    service = production_services.get(service_name, {})
    environment = service.get("environment", {})
    if environment.get("ASPNETCORE_ENVIRONMENT") != "Production":
        fail(f"production: {service_name} must use Production")
    if environment.get("DEPLOYMENT__HARDENED") != "true":
        fail(f"production: {service_name} does not enable hardened deployment validation")
    if environment.get("SECURITY__REQUIREHTTPSCOOKIES") != "true":
        fail(f"production: {service_name} does not require secure cookies")
    if environment.get("ASPNETCORE_HTTPS_PORT") != "443":
        fail(f"production: {service_name} does not define the public HTTPS port")
    if environment.get("DATAPROTECTION__KEYPATH") != "/keys":
        fail(f"production: {service_name} does not define persistent Data Protection keys")
    if not any(volume.get("type") == "volume" and volume.get("target") == "/keys"
               for volume in service.get("volumes", [])):
        fail(f"production: {service_name} does not mount the Data Protection volume")

if published_ports(production_services.get("migrate", {})):
    fail("production: migrate must not publish ports")
