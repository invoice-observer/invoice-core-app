# Postman API Collections

Postman collections and environments for API workflow testing.

## Prerequisites

### Install Postman
   - Visit [postman.com/downloads](https://www.postman.com/downloads/)
   - Download the installer (Windows, macOS, or Linux)
   - (Windows) Choose the `.exe` file for Windows installation, run it and follow the prompts
   - Or continue as a guest for local use only

## Files Included

### Collections
- `InvoiceAPI.postman_collection.json` - Complete test suite for Invoice Core Application

### Environments  
- `Local.postman_environment.json` - Local development environment (https://localhost:7052)

## Test Coverage

### Authentication Tests
- **JWT Login** - Test successful authentication with the `auth_username` / `auth_password` credentials
- **Invalid Credentials** - Test authentication failure with wrong credentials

### Invoice Tests
- **Unauthorized Access** - Test invoice creation without authentication
- **Get All Invoices** - Retrieve all invoices (requires authentication)
- **Create Invoice** - Create new invoice with invoice lines (requires authentication)

### Complete Workflow
- **End-to-End Test** - Complete workflow: Login → Create Invoice → JWT Logout.
- **JWT Logout** - Clears `jwt_token` from environment and collection variables, then verifies invoice access without a token returns `401`. Run this step last; log in again before running authenticated requests. This is client-side logout and does not revoke issued tokens.

## How to Use

1. **Import Collection**
   - Open Postman
   - Click "Import"
   - Select `Collections/InvoiceAPI.postman_collection.json`

2. **Import Environment**
   - Click "Import" 
   - Select `Environments/Local.postman_environment.json`
   - Set as active environment
   - Set `auth_username` and `auth_password` to the API's `Auth:UserName` / `Auth:Password`
     (enter them in the **Current value** column only, so they stay local and are never exported or synced)

3. **Run Tests**
   - **Individual Tests**: Select any request and click "Send"
   - **Full Collection**: Use Collection Runner to run all tests
   - **Workflow**: Run requests in "Complete Workflow" folder in order

## Test Variables

The collection uses these environment variables:
- `base_url` - API base URL (automatically set by environment)
- `auth_username` - Login user name (set manually; must match `Auth:UserName`)
- `auth_password` - Login password (set manually, secret; must match `Auth:Password`)
- `jwt_token` - JWT token (automatically set after login)  
- `invoice_id` - Created invoice ID (automatically set after creation)

## Expected Results

### Successful Login Response
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

### Successful Invoice Creation Response  
```json
{
  "id": 1,
  "description": "Test Invoice from Postman", 
  "dueDate": "2024-02-15T00:00:00",
  "supplier": "Postman Test Supplier",
  "invoiceLines": [...]
}
```

## Authentication Credentials
Credentials are not stored in the collection or in Git. They come from the `auth_username` / `auth_password`
environment variables, which must match the API's `Auth:UserName` / `Auth:Password` configuration
(see [Configuration and Secrets](../../README.md#configuration-and-secrets)).

