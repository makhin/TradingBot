#!/bin/bash
# Load environment variables from .env file
# Usage: source load-env.sh
# Or:    . ./load-env.sh

ENV_FILE="${1:-.env}"
SHOW_VARS="${2:-false}"
VALIDATE="${3:-false}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Check if .env file exists
if [ ! -f "$ENV_FILE" ]; then
    echo -e "${RED}❌ Error: .env file not found at '$ENV_FILE'${NC}"
    echo -e "${YELLOW}📝 Create .env from template: cp .env.example .env${NC}"
    return 1 2>/dev/null || exit 1
fi

# Load variables
loaded_count=0
declare -A loaded_vars

while IFS= read -r line; do
    # Skip empty lines and comments
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue

    # Skip lines without = sign
    [[ ! "$line" =~ = ]] && continue

    # Extract key and value
    key="${line%=*}"
    value="${line#*=}"

    # Trim whitespace
    key="$(echo "$key" | xargs)"
    value="$(echo "$value" | xargs)"

    # Remove quotes if present
    value="${value%\"}"
    value="${value#\"}"

    # Export variable
    export "$key=$value"
    loaded_vars["$key"]="$value"
    ((loaded_count++))

    # Display if requested (hide secrets)
    if [ "$SHOW_VARS" = "true" ]; then
        if [[ "$key" =~ SECRET|TOKEN|PASSWORD ]]; then
            echo -e "  ${GREEN}✓${NC} $key = ***"
        else
            echo -e "  ${GREEN}✓${NC} $key = $value"
        fi
    fi
done < "$ENV_FILE"

echo -e "${GREEN}✅ Loaded $loaded_count environment variables from $ENV_FILE${NC}"

# Validate required variables
if [ "$VALIDATE" = "true" ]; then
    echo -e "\n${CYAN}🔍 Validating required variables...${NC}"

    required_vars=("BINANCE_TESTNET_KEY" "BINANCE_TESTNET_SECRET")
    missing_vars=()

    for var in "${required_vars[@]}"; do
        value="${!var}"

        if [ -z "$value" ] || [[ "$value" == your-* ]]; then
            missing_vars+=("$var")
            echo -e "  ${RED}❌ $var - NOT SET${NC}"
        else
            echo -e "  ${GREEN}✅ $var - OK${NC}"
        fi
    done

    if [ ${#missing_vars[@]} -gt 0 ]; then
        echo -e "\n${YELLOW}⚠️  Missing variables: ${missing_vars[*]}${NC}"
        echo -e "${YELLOW}📝 Edit .env and fill in actual values (not placeholders)${NC}"
        return 1 2>/dev/null || exit 1
    fi
fi

echo -e "\n${CYAN}✨ Ready to run: dotnet run${NC}"
return 0 2>/dev/null || true
