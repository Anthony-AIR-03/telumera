// Example parameters for main.bicep's dev environment. No secrets or account-specific values here —
// the subscription/tenant come from whatever `az` session is logged in at deploy time, not from this
// file. Copy and adjust environmentName/location for a different environment (e.g. staging.bicepparam).
using 'main.bicep'

param environmentName = 'dev'
param location = 'westeurope'
