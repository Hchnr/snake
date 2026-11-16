The optional `ReferenceSkins/` directory is local-only and ignored by Git. It
contains unlicensed character likenesses for internal review, not distributable
game assets. Normal player builds do not include this directory. An explicit
`SNAKE_INCLUDE_REFERENCE_ASSETS=1` internal build can stage locally available
images temporarily; that build is not a release artifact.
