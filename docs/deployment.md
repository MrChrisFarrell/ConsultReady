# Render deployment

`render.yaml` configures one Docker web service on the Starter plan with a 1 GB persistent disk at `/var/data`. This is a paid configuration: review Render's displayed recurring price before provisioning. No paid service has been provisioned by this implementation.

1. Connect the public GitHub repository in Render and create a Blueprint from `render.yaml`.
2. Review the service and disk charge. Enter `MODEL_API_KEY` as a secret in Render; never paste it into tracked files.
3. Deploy. The container serves UI and API on port 8080. Health path: `/health`.
4. Open the assigned HTTPS URL. Confirm the badge says **Synthetic public demo** and arbitrary upload controls are absent.
5. Run all three samples with live Meta. Confirm citations, review gates, and the $3,000 → $2,000 revision. Restart the service and verify the saved synthetic case remains available with the same browser session.
6. Verify another browser session cannot access the first session's case URL.
7. Record the live URL and the tested commit in the submission. Keep the service available through judging.

The runtime entrypoint sets disk-root ownership and then runs .NET as the unprivileged `app` user. Persistent SQLite storage requires one instance; do not scale replicas against this disk. The filesystem outside the mounted disk is disposable. Hosted cookies require HTTPS, so an HTTP-only local container is not a valid hosted-session test.

For local container testing with real uploads disabled by default, use a TLS reverse proxy. For loopback-only local mode, override `ConsultReady__Mode=Local` and publish only to 127.0.0.1; Docker bridge networking may not count as loopback, so the native local command in README is the recommended private workflow.

Credentials and external dependencies still needed: a working Meta API key, a Render account with approved hosting charges, and a live model smoke test. The Docker engine was unavailable on the development machine; CI performs the container build.
