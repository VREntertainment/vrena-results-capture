# Private support bundle retrieval

This runbook is for an authorized Codex operator after the venue user has explicitly chosen **Upload support bundle** in the Windows app and asks Codex to retrieve it.

The permanent storage bucket is private. Neither the Windows import token nor a public URL can download a bundle.

1. Query `public.venue_support_bundles` through the connected Supabase project and select the requested bundle ID, normally the most recent upload.
2. Generate a fresh 32-byte random token locally. Keep only its SHA-256 digest in the database.
3. Insert the digest, bundle ID, and an expiry no more than 10 minutes in the future into `public.venue_support_bundle_download_tokens`.
4. POST `{"bundleId":"..."}` to `/api/venue/support-bundles/download` with the plaintext token in `X-VRena-Support-Token`.
5. Download the returned signed URL within 60 seconds. The database token is consumed by the first successful validation and cannot be reused.
6. Treat the downloaded ZIP as private support material. It may contain player names, machine information, and local paths.

Never store the plaintext retrieval token in the repository, logs, database, or a support response.
