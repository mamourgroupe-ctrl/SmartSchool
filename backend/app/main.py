from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api.v1.ai import router as ai_router
from app.api.v1.auth import router as auth_router
from app.core.config import settings


app = FastAPI(
    title="SmartSchool API",
    description="Backend API for SmartSchool",
    version="1.0.0",
)

# CORS is restricted to the configured origin list (no wildcard with credentials).
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origin_list(),
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(
    ai_router,
    prefix="/api/v1",
)

app.include_router(
    auth_router,
    prefix="/api/v1",
)


@app.get("/")
async def root():
    return {
        "message": "SmartSchool API is running",
        "status": "ok",
        "version": "1.0.0",
    }


@app.get("/health")
async def health():
    return {
        "status": "healthy",
        "service": "smartschool-backend",
    }
