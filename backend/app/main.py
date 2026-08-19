from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api.v1.ai import router as ai_router
from app.api.v1.auth import router as auth_router


app = FastAPI(
    title="SmartSchool API",
    description="Backend API for SmartSchool",
    version="1.0.0",
)

# Add CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
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
    prefix="/api",
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
