#!/bin/bash
# =============================================================================
# Meridian Setup Scripts Verification Test Suite
# =============================================================================
#
# Tests for setup-ai-agent.sh and install.sh
# Usage: ./tests/scripts/setup-verification.sh
#

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test counters
TESTS_PASSED=0
TESTS_FAILED=0

# Helper functions
print_test() {
    echo -e "${BLUE}[TEST]${NC} $1"
}

print_pass() {
    echo -e "${GREEN}[PASS]${NC} $1"
    TESTS_PASSED=$((TESTS_PASSED + 1))
}

print_fail() {
    echo -e "${RED}[FAIL]${NC} $1"
    TESTS_FAILED=$((TESTS_FAILED + 1))
}

print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_section() {
    echo ""
    echo "=== $1 ==="
    echo ""
}

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

cd "$PROJECT_ROOT"

print_section "Setup Scripts Verification Tests"

# =============================================================================
# Test Suite 1: setup-ai-agent.sh
# =============================================================================
print_section "Suite 1: setup-ai-agent.sh Tests"

print_test "Test 1.1: Script is executable"
if [ -x "./scripts/ai/setup-ai-agent.sh" ]; then
    print_pass "setup-ai-agent.sh is executable"
else
    print_fail "setup-ai-agent.sh is not executable"
fi

print_test "Test 1.2: Basic execution without arguments"
set +e
./scripts/ai/setup-ai-agent.sh > /tmp/setup_test.log 2>&1
result=$?
set -e
if [ $result -eq 0 ] && grep -q "AI agent environment ready" /tmp/setup_test.log; then
    print_pass "Script runs successfully and outputs expected message"
else
    print_fail "Script execution failed or missing expected output"
fi

print_test "Test 1.3: Environment file is created"
if [ -f ".ai/env.sh" ]; then
    print_pass "Environment file .ai/env.sh created"
else
    print_fail "Environment file not created"
fi

print_test "Test 1.4: Environment file has correct permissions"
if [ -x ".ai/env.sh" ]; then
    print_pass "Environment file is executable"
else
    print_fail "Environment file is not executable"
fi

print_test "Test 1.5: Environment file contains DOTNET_ROOT export"
if grep -q "DOTNET_ROOT" .ai/env.sh; then
    print_pass "Environment file contains DOTNET_ROOT export"
else
    print_fail "Environment file missing DOTNET_ROOT export"
fi

print_test "Test 1.6: Environment file contains PATH export"
if grep -q "export PATH=" .ai/env.sh; then
    print_pass "Environment file contains PATH export"
else
    print_fail "Environment file missing PATH export"
fi

print_test "Test 1.7: Invalid flag handling"
set +e
./scripts/ai/setup-ai-agent.sh --invalid-flag > /tmp/setup_invalid.log 2>&1
result=$?
set -e
if [ $result -ne 0 ] && grep -q "Unknown option" /tmp/setup_invalid.log; then
    print_pass "Script correctly rejects invalid flags"
else
    print_fail "Script does not properly reject invalid flags"
fi

# =============================================================================
# Test Suite 2: install.sh
# =============================================================================
print_section "Suite 2: install.sh Tests"

print_test "Test 2.1: Script is executable"
if [ -x "./build/scripts/install/install.sh" ]; then
    print_pass "install.sh is executable"
else
    print_fail "install.sh is not executable"
fi

print_test "Test 2.2: Help flag displays correctly"
set +e
./build/scripts/install/install.sh --help > /tmp/install_help.log 2>&1
result=$?
set -e
if [ $result -eq 0 ] && grep -q "Installation Script" /tmp/install_help.log; then
    print_pass "Help text displays correctly"
else
    print_fail "Help text missing expected content"
fi

print_test "Test 2.3: All expected help options are documented"
if grep -qE "(--docker|--native|--check|--uninstall|--help)" /tmp/install_help.log; then
    print_pass "All installation options documented in help"
else
    print_fail "Some installation options missing from help"
fi

print_test "Test 2.4: Invalid flag handling"
set +e
./build/scripts/install/install.sh --invalid-option > /tmp/install_invalid.log 2>&1
result=$?
set -e
if [ $result -ne 0 ] && grep -q "Unknown option" /tmp/install_invalid.log; then
    print_pass "Script correctly rejects invalid options"
else
    print_fail "Script does not properly reject invalid options"
fi

# =============================================================================
# Test Suite 3: Integration Tests
# =============================================================================
print_section "Suite 3: Integration Tests"

print_test "Test 3.1: Environment can be sourced and used"
set +e
result=$(bash -c "source .ai/env.sh && echo \$DOTNET_ROOT")
set -e
if [ -n "$result" ] && [ "$result" = "/root/.dotnet" ]; then
    print_pass "Environment setup works correctly with sourcing"
else
    print_fail "Environment sourcing did not work as expected (got: $result)"
fi

print_test "Test 3.2: dotnet is available after environment setup"
set +e
bash -c "source .ai/env.sh && command -v dotnet >/dev/null 2>&1"
result=$?
set -e
if [ $result -eq 0 ]; then
    print_pass "dotnet command available after sourcing env.sh"
else
    print_fail "dotnet command not available after sourcing env.sh"
fi

print_test "Test 3.3: dotnet version can be retrieved"
set +e
dotnet_version=$(bash -c "source .ai/env.sh && dotnet --version 2>&1")
result=$?
set -e
if [ $result -eq 0 ] && [[ "$dotnet_version" =~ ^[0-9]+\.[0-9] ]]; then
    print_pass "dotnet version retrieval works: $dotnet_version"
else
    print_fail "Failed to retrieve dotnet version"
fi

# =============================================================================
# Summary
# =============================================================================
print_section "Test Summary"

echo "Tests Passed: ${GREEN}${TESTS_PASSED}${NC}"
echo "Tests Failed: ${RED}${TESTS_FAILED}${NC}"
echo "Total Tests:  $((TESTS_PASSED + TESTS_FAILED))"
echo ""

if [ $TESTS_FAILED -eq 0 ]; then
    echo -e "${GREEN}✓ All tests passed!${NC}"
    exit 0
else
    echo -e "${RED}✗ Some tests failed.${NC}"
    exit 1
fi
