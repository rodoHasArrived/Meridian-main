cmake_minimum_required(VERSION 3.20)
project(MeridianCppTraderHost VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ── nlohmann/json ────────────────────────────────────────────────────────────
# Prefer a system-installed copy; fall back to FetchContent for developer
# environments that do not have it pre-installed.
find_package(nlohmann_json 3.10 QUIET)
if(NOT nlohmann_json_FOUND)
    include(FetchContent)
    FetchContent_Declare(
        nlohmann_json
        URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
        URL_HASH SHA256=d6c65aca6b1ed68e7a182f4757257b107ae403032760ed6ef121c9d55e81757d
    )
    FetchContent_MakeAvailable(nlohmann_json)
endif()

# ── executable ───────────────────────────────────────────────────────────────
add_executable(meridian-cpptrader-host
    src/main.cpp
)

target_include_directories(meridian-cpptrader-host PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_link_libraries(meridian-cpptrader-host PRIVATE
    nlohmann_json::nlohmann_json
)

# ── CppTrader integration (vendored, optional) ───────────────────────────────
# When the upstream CppTrader library is checked out under native/vendor/CppTrader,
# uncomment the block below to enable the real matching engine.
#
#   git submodule add https://github.com/chronoxor/CppTrader.git native/vendor/CppTrader
#
# if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../vendor/CppTrader/CMakeLists.txt")
#     add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../vendor/CppTrader" cpptrader_build)
#     target_link_libraries(meridian-cpptrader-host PRIVATE CppTrader)
#     target_compile_definitions(meridian-cpptrader-host PRIVATE MERIDIAN_CPPTRADER_NATIVE=1)
# endif()

# ── compiler warnings ────────────────────────────────────────────────────────
if(MSVC)
    target_compile_options(meridian-cpptrader-host PRIVATE /W4 /WX)
else()
    target_compile_options(meridian-cpptrader-host PRIVATE -Wall -Wextra -Wpedantic)
endif()

# ── install ───────────────────────────────────────────────────────────────────
install(TARGETS meridian-cpptrader-host
    RUNTIME DESTINATION bin
)
