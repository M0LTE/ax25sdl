# Hand-written build for the generated C SDL tables. Lives outside the
# *.g.{c,h} codegen globs. Builds the generated pages into a static library
# and compiles each generated per-page test as a ctest executable.
cmake_minimum_required(VERSION 3.16)
project(ax25sdl C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
add_compile_options(-Wall -Wextra -Werror)

# Generated SDL page tables -> static library. ax25sdl.h (hand-written) sits
# alongside the generated sources in src/ and is the public include.
file(GLOB AX25SDL_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/*.g.c")
add_library(ax25sdl STATIC ${AX25SDL_SOURCES})
target_include_directories(ax25sdl PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/src")

# One ctest per generated per-page test executable. Each test source carries
# its own main() and externs the page symbol it checks; linking the library
# satisfies that extern.
enable_testing()
file(GLOB AX25SDL_TESTS CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/test/*.g.test.c")
foreach(test_src ${AX25SDL_TESTS})
  get_filename_component(test_name "${test_src}" NAME_WE)
  add_executable(test_${test_name} "${test_src}")
  target_link_libraries(test_${test_name} PRIVATE ax25sdl)
  add_test(NAME ${test_name} COMMAND test_${test_name})
endforeach()
