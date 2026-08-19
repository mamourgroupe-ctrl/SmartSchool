from fastapi import APIRouter, HTTPException, status
from app.schemas.auth import LoginRequest, LoginResponse, LoginError


router = APIRouter(
    prefix="/auth",
    tags=["Authentication"],
)


@router.post("/login", response_model=LoginResponse, responses={401: {"model": LoginError}})
async def login(request: LoginRequest):
    # Basic authentication check as per user request
    if request.username == "admin" and request.password == "password":
        return LoginResponse(
            token="jwt-sample-token-12345",
            role="Admin",
            message="Login successful"
        )

    raise HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Invalid username or password"
    )
