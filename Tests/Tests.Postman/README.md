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
- **JWT Login** - Test successful authentication with Admin/onventis credentials
- **Invalid Credentials** - Test authentication failure with wrong credentials

### Invoice Tests
- **Get All Invoices** - Retrieve all invoices (requires authentication)
- **Create Invoice** - Create new invoice with invoice lines (requires authentication)
- **Get Invoice by ID** - Retrieve specific invoice by ID
- **Unauthorized Access** - Test invoice creation without authentication

### Complete Workflow
- **End-to-End Test** - Complete workflow: Login → Create Invoice → Verify Invoice

## How to Use

1. **Import Collection**
   - Open Postman
   - Click "Import"
   - Select `Collections/InvoiceAPI.postman_collection.json`

2. **Import Environment**
   - Click "Import" 
   - Select `Environments/Local.postman_environment.json`
   - Set as active environment

3. **Run Tests**
   - **Individual Tests**: Select any request and click "Send"
   - **Full Collection**: Use Collection Runner to run all tests
   - **Workflow**: Run requests in "Complete Workflow" folder in order

## Test Variables

The collection uses these environment variables:
- `base_url` - API base URL (automatically set by environment)
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
- **Username**: Admin
- **Password**: onventis

